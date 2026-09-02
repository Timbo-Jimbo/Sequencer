using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Builder;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimboTests.Sequencer
{
    public sealed class SequencePlayerLifecycleTests
    {
        private readonly List<SequencePlayer> _players = new();
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("SequencePlayer lifecycle target");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var player in _players)
                player.Dispose();

            Object.DestroyImmediate(_target);
        }

        [Test]
        public void CreatePlayer_UsesIdleManualDefaults()
        {
            var player = CreatePlayer(2f);

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Idle));
            Assert.That(player.Playhead, Is.Zero);
            Assert.That(player.Duration, Is.EqualTo(2f));
            Assert.That(player.IsAutoDriven, Is.False);
            Assert.That(player.IsPreview, Is.False);
        }

        [Test]
        public void PlayPauseResumeStop_TransitionsAndRaisesEventsInOrder()
        {
            var events = new List<string>();
            var player = CreatePlayer(2f);
            player.Started += _ => events.Add("started");
            player.Paused += _ => events.Add("paused");
            player.Resumed += _ => events.Add("resumed");
            player.Stopped += _ => events.Add("stopped");

            player.Play();
            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Playing));

            player.Tick(0.5f);
            player.Pause();
            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Paused));

            player.Resume();
            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Playing));

            player.Stop();
            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Stopped));
            Assert.That(player.Playhead, Is.Zero);
            Assert.That(events, Is.EqualTo(new[] { "started", "paused", "resumed", "stopped" }));
        }

        [Test]
        public void ResumeFromIdle_StartsRatherThanResumes()
        {
            var startedCount = 0;
            var resumedCount = 0;
            var player = CreatePlayer();
            player.Started += _ => startedCount++;
            player.Resumed += _ => resumedCount++;

            player.Resume();

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Playing));
            Assert.That(startedCount, Is.EqualTo(1));
            Assert.That(resumedCount, Is.Zero);
        }

        [Test]
        public void InvalidStateTransitions_DoNotRaiseDuplicateEvents()
        {
            var pausedCount = 0;
            var resumedCount = 0;
            var stoppedCount = 0;
            var player = CreatePlayer();
            player.Paused += _ => pausedCount++;
            player.Resumed += _ => resumedCount++;
            player.Stopped += _ => stoppedCount++;

            player.Pause();
            player.Play();
            player.Resume();
            player.Pause();
            player.Pause();
            player.Stop();
            player.Stop();

            Assert.That(pausedCount, Is.EqualTo(1));
            Assert.That(resumedCount, Is.Zero);
            Assert.That(stoppedCount, Is.EqualTo(1));
        }

        [Test]
        public void Tick_OnlyAdvancesWhilePlayingAndScalesBySpeed()
        {
            var player = CreatePlayer(4f);

            player.Tick(1f);
            Assert.That(player.Playhead, Is.Zero);

            player.Speed = 0.5f;
            player.Play();
            player.Tick(2f);
            Assert.That(player.Playhead, Is.EqualTo(1f).Within(0.0001f));

            player.Pause();
            player.Tick(1f);
            Assert.That(player.Playhead, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void ZeroAndNegativeSpeed_DoNotAdvance()
        {
            var player = CreatePlayer();
            player.Play();

            player.Speed = 0f;
            player.Tick(1f);
            Assert.That(player.Playhead, Is.Zero);

            player.Speed = -5f;
            player.Tick(1f);
            Assert.That(player.Speed, Is.Zero);
            Assert.That(player.Playhead, Is.Zero);
        }

        [Test]
        public void NonLoopingPlayer_CompletesOnceAtRangeEnd()
        {
            var events = new List<string>();
            var player = CreatePlayer(2f);
            player.Started += _ => events.Add("started");
            player.Completed += _ => events.Add("completed");

            player.Play();
            player.Tick(3f);
            player.Tick(1f);

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Completed));
            Assert.That(player.Playhead, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(events, Is.EqualTo(new[] { "started", "completed" }));
        }

        [Test]
        public void LoopingPlayer_OversizedTickRaisesOneEventPerWrap()
        {
            var loopCount = 0;
            var player = CreatePlayer();
            player.Loop = true;
            player.Looped += _ => loopCount++;

            player.Play();
            player.Tick(3.5f);

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Playing));
            Assert.That(player.Playhead, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(loopCount, Is.EqualTo(3));
        }

        [Test]
        public void PlaybackRange_StartsAtStartAndCompletesAtEnd()
        {
            var player = CreatePlayer(2f, new PlaybackRange(0.5f, 1.25f));

            player.Play();
            Assert.That(player.Playhead, Is.EqualTo(0.5f).Within(0.0001f));

            player.Tick(1f);
            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Completed));
            Assert.That(player.Playhead, Is.EqualTo(1.25f).Within(0.0001f));
        }

        [Test]
        public void ZeroLengthPlaybackRange_CompletesWithoutLooping()
        {
            var completedCount = 0;
            var loopedCount = 0;
            var player = CreatePlayer(2f, new PlaybackRange(1f, 1f));
            player.Loop = true;
            player.Completed += _ => completedCount++;
            player.Looped += _ => loopedCount++;

            player.Play();
            player.Tick(0.5f);

            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Completed));
            Assert.That(player.Playhead, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(completedCount, Is.EqualTo(1));
            Assert.That(loopedCount, Is.Zero);
        }

        [Test]
        public void AnchoredPlaybackRange_UsesTimelineEnd()
        {
            var player = CreatePlayer(2f, new PlaybackRange(0.5f, 100f, endAnchoredToTimelineEnd: true));

            Assert.That(player.PlaybackRange.GetResolvedStart(), Is.EqualTo(0.5f));
            Assert.That(player.PlaybackRange.GetResolvedEnd(player.Duration), Is.EqualTo(2f));

            player.Play();
            player.Tick(1.5f);
            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Completed));
        }

        [Test]
        public void Dispose_RestoresCapturedInitialValue()
        {
            var initial = new Vector3(2f, 3f, 4f);
            var changed = new Vector3(5f, 6f, 7f);
            _target.transform.localPosition = initial;
            var player = CreateSetterPlayer(changed, restoreValuesOnDispose: true);

            player.Play();
            player.Tick(0.5f);
            Assert.That(_target.transform.localPosition, Is.EqualTo(changed));

            player.Dispose();
            Assert.That(_target.transform.localPosition, Is.EqualTo(initial));
        }

        [Test]
        public void DisposeWithoutRestore_RetainsWrittenValue()
        {
            var changed = new Vector3(5f, 6f, 7f);
            var player = CreateSetterPlayer(changed, restoreValuesOnDispose: false);

            player.Play();
            player.Tick(0.5f);
            player.Dispose();

            Assert.That(_target.transform.localPosition, Is.EqualTo(changed));
        }

        [Test]
        public void Dispose_IsIdempotentAndFurtherOperationsAreNoOps()
        {
            var eventCount = 0;
            var player = CreatePlayer(2f);
            player.Play();
            player.Tick(0.5f);
            player.Started += _ => eventCount++;
            player.Paused += _ => eventCount++;
            player.Resumed += _ => eventCount++;
            player.Stopped += _ => eventCount++;

            player.Dispose();
            var state = player.State;
            var playhead = player.Playhead;

            Assert.DoesNotThrow(() =>
            {
                player.Dispose();
                player.Play();
                player.Pause();
                player.Resume();
                player.Stop();
                player.Tick(1f);
                player.Seek(1f);
                player.SetPlaybackRange(new PlaybackRange(0f, 1f));
                player.ClearPlaybackRange();
            });

            Assert.That(player.State, Is.EqualTo(state));
            Assert.That(player.Playhead, Is.EqualTo(playhead));
            Assert.That(eventCount, Is.Zero);
        }

        [Test]
        public void SegmentExtensions_DistinguishManualAndSelfDrivenPlayers()
        {
            var segment = new TestDurationSegment { Duration = 10f };
            var manual = Track(segment.CreatePlayer());
            var selfDriven = Track(segment.Play(loop: true, speed: 0.5f));

            Assert.That(manual.IsAutoDriven, Is.False);
            Assert.That(manual.State, Is.EqualTo(SequencePlaybackState.Idle));
            Assert.That(selfDriven.IsAutoDriven, Is.True);
            Assert.That(selfDriven.State, Is.EqualTo(SequencePlaybackState.Playing));
            Assert.That(selfDriven.Loop, Is.True);
            Assert.That(selfDriven.Speed, Is.EqualTo(0.5f));
        }

        private SequencePlayer CreatePlayer(float duration = 1f)
        {
            return Track(new TestDurationSegment { Duration = duration }.CreatePlayer());
        }

        private SequencePlayer CreatePlayer(float duration, PlaybackRange range)
        {
            return Track(new TestDurationSegment { Duration = duration }.CreatePlayer(range));
        }

        private SequencePlayer CreateSetterPlayer(Vector3 value, bool restoreValuesOnDispose)
        {
            var setter = Seq.Make.SetPosition(_target.transform, value);
            setter.StartTime = 0.5f;
            setter.PreExtrapolation = PreExtrapolationMode.None;
            return Track(setter.CreatePlayer(restoreValuesOnDispose: restoreValuesOnDispose));
        }

        private SequencePlayer Track(SequencePlayer player)
        {
            _players.Add(player);
            return player;
        }
    }

    internal sealed class TestDurationSegment : Segment, IPlaybackBuilder
    {
        public float Duration;

        public override SegmentPlan GetPlan(SegmentPlan parent)
        {
            return new SegmentPlan(this, parent)
            {
                Timing = { RelativeDuration = Duration }
            };
        }

        public SegmentPlayback BuildPlayback(in PlaybackBuildContext context)
        {
            return new TestDurationPlayback(in context);
        }

        private sealed class TestDurationPlayback : SegmentPlayback
        {
            public TestDurationPlayback(in PlaybackBuildContext context) : base(in context) { }
        }
    }
}