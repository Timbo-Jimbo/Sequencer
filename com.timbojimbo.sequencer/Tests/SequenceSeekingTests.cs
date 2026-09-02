using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Builder;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimboTests.Sequencer
{
    public sealed class SequenceSeekingTests
    {
        private readonly List<SequencePlayer> _players = new();
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("SequencePlayer seeking target");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var player in _players)
                player.Dispose();

            Object.DestroyImmediate(_target);
        }

        [Test]
        public void ForwardSeek_EvaluatesIntermediatePropertyValue()
        {
            var player = CreateTweenPlayer();

            player.Seek(1f);

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Idle));
            Assert.That(player.Playhead, Is.EqualTo(1f).Within(0.0001f));
            AssertPositionX(5f);
        }

        [Test]
        public void BackwardSeek_ResetsAndDeterministicallyEvaluatesForward()
        {
            var player = CreateTweenPlayer();
            player.Play();
            player.Tick(1.5f);
            AssertPositionX(7.5f);

            player.Seek(0.5f);

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Playing));
            Assert.That(player.Playhead, Is.EqualTo(0.5f).Within(0.0001f));
            AssertPositionX(2.5f);
        }

        [Test]
        public void SeekFromCompleted_TransitionsToPausedWithoutLifecycleEvent()
        {
            var eventCount = 0;
            var player = CreateTweenPlayer();
            player.Play();
            player.Tick(2f);
            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Completed));
            player.Started += _ => eventCount++;
            player.Paused += _ => eventCount++;
            player.Resumed += _ => eventCount++;
            player.Stopped += _ => eventCount++;
            player.Completed += _ => eventCount++;

            player.Seek(0.5f);

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Paused));
            Assert.That(player.Playhead, Is.EqualTo(0.5f).Within(0.0001f));
            AssertPositionX(2.5f);
            Assert.That(eventCount, Is.Zero);
        }

        [Test]
        public void SeekFromStopped_TransitionsToPausedAndEvaluates()
        {
            var player = CreateTweenPlayer();
            player.Play();
            player.Tick(1f);
            player.Stop();

            player.Seek(0.5f);

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Paused));
            Assert.That(player.Playhead, Is.EqualTo(0.5f).Within(0.0001f));
            AssertPositionX(2.5f);
        }

        [Test]
        public void Seek_ClampsToFullTimelineBounds()
        {
            var player = CreateTweenPlayer();

            player.Seek(-1f);
            Assert.That(player.Playhead, Is.Zero);

            player.Seek(10f);
            Assert.That(player.Playhead, Is.EqualTo(2f).Within(0.0001f));
            AssertPositionX(10f);
        }

        [Test]
        public void ExactBoundary_IsProcessedOncePerForwardTraversal()
        {
            var triggerCount = 0;
            var sequence = new Sequence();
            sequence.Add(new TestDurationSegment { Duration = 2f });
            var callback = Seq.Make.Callback(() => triggerCount++);
            callback.TriggerAt = 1f;
            sequence.Add(callback);
            var player = Track(sequence.CreatePlayer());

            player.Seek(1f);
            player.Seek(1f);
            Assert.That(triggerCount, Is.EqualTo(1));

            player.Seek(0.5f);
            player.Seek(1f);
            Assert.That(triggerCount, Is.EqualTo(2));
        }

        [Test]
        public void EqualTimeKeyframes_PreserveSegmentDiscoveryOrder()
        {
            var sequence = new Sequence();
            var first = Seq.Make.SetPosition(_target.transform, Vector3.one);
            first.StartTime = 1f;
            first.PreExtrapolation = PreExtrapolationMode.None;
            var second = Seq.Make.SetPosition(_target.transform, Vector3.one * 2f);
            second.StartTime = 1f;
            second.PreExtrapolation = PreExtrapolationMode.None;
            sequence.Add(first);
            sequence.Add(second);
            sequence.Add(new TestDurationSegment { Duration = 2f });
            var player = Track(sequence.CreatePlayer());

            player.Seek(1f);

            Assert.That(_target.transform.localPosition, Is.EqualTo(Vector3.one * 2f));
        }

        [Test]
        public void Seek_WithPlaybackRange_RemainsAbsoluteWithinTimeline()
        {
            var player = Track(CreateTween().CreatePlayer(new PlaybackRange(0.5f, 1.5f)));
            player.Play();
            Assert.That(player.Playhead, Is.EqualTo(0.5f).Within(0.0001f));

            player.Seek(1.25f);

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Playing));
            Assert.That(player.Playhead, Is.EqualTo(1.25f).Within(0.0001f));
            AssertPositionX(6.25f);

            player.Tick(1f);
            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Completed));
            Assert.That(player.Playhead, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void PlayAfterCompletion_ReplaysFromBeginning()
        {
            var startedCount = 0;
            var player = CreateTweenPlayer();
            player.Started += _ => startedCount++;
            player.Play();
            player.Tick(2f);

            player.Play();

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Playing));
            Assert.That(player.Playhead, Is.Zero);
            Assert.That(startedCount, Is.EqualTo(2));
            player.Tick(1f);
            AssertPositionX(5f);
        }

        [Test]
        public void PlaybackContinuesAfterBackwardSeek()
        {
            var player = CreateTweenPlayer();
            player.Play();
            player.Tick(1.5f);
            player.Seek(0.5f);

            player.Tick(0.5f);

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Playing));
            Assert.That(player.Playhead, Is.EqualTo(1f).Within(0.0001f));
            AssertPositionX(5f);
        }

        private SequencePlayer CreateTweenPlayer()
        {
            return Track(CreateTween().CreatePlayer());
        }

        private PropertyTweener CreateTween()
        {
            return Seq.Make.TweenPosition(
                _target.transform,
                TweenStart.Absolute(Vector3.zero),
                TweenEnd.Absolute(new Vector3(10f, 0f, 0f)),
                2f);
        }

        private void AssertPositionX(float expected)
        {
            Assert.That(_target.transform.localPosition.x, Is.EqualTo(expected).Within(0.0001f));
        }

        private SequencePlayer Track(SequencePlayer player)
        {
            _players.Add(player);
            return player;
        }
    }
}