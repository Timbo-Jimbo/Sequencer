using System.Collections;
using NUnit.Framework;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Builder;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;
using UnityEngine.TestTools;

namespace TimboJimboTests.Sequencer.PlayMode
{
    public sealed class SequenceDriverPlayModeTests
    {
        private GameObject _target;
        private SequencePlayer _player;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _target = new GameObject("SequenceDriver Play Mode target");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _player?.Dispose();
            Object.Destroy(_target);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SelfDrivenPlayback_AdvancesPausesAndResumesThroughPlayerLoop()
        {
            _player = CreateTween(duration: 10f).Play();
            Assert.That(_player.IsAutoDriven, Is.True);
            Assert.That(_player.State, Is.EqualTo(SequencePlaybackState.Playing));

            var initialPlayhead = _player.Playhead;
            yield return null;
            yield return null;
            Assert.That(_player.Playhead, Is.GreaterThan(initialPlayhead));

            _player.Pause();
            var pausedPlayhead = _player.Playhead;
            yield return null;
            yield return null;
            Assert.That(_player.State, Is.EqualTo(SequencePlaybackState.Paused));
            Assert.That(_player.Playhead, Is.EqualTo(pausedPlayhead).Within(0.000001f));

            _player.Resume();
            yield return null;
            yield return null;
            Assert.That(_player.State, Is.EqualTo(SequencePlaybackState.Playing));
            Assert.That(_player.Playhead, Is.GreaterThan(pausedPlayhead));
        }

        [UnityTest]
        public IEnumerator NonLoopingPlayback_CompletesOnceAndStopsAdvancing()
        {
            var completedCount = 0;
            _player = CreateTween(duration: 0.01f).Play();
            _player.Completed += _ => completedCount++;

            for (int frame = 0; frame < 20 && !_player.IsComplete; frame++)
                yield return null;

            Assert.That(_player.State, Is.EqualTo(SequencePlaybackState.Completed));
            Assert.That(completedCount, Is.EqualTo(1));
            var completedPlayhead = _player.Playhead;
            yield return null;
            yield return null;
            Assert.That(_player.Playhead, Is.EqualTo(completedPlayhead).Within(0.000001f));
            Assert.That(completedCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Dispose_UnregistersPlayerAndRestoresCapturedValue()
        {
            var initial = new Vector3(2f, 3f, 4f);
            _target.transform.localPosition = initial;
            _player = CreateTween(duration: 10f).Play();

            yield return null;
            yield return null;
            Assert.That(_target.transform.localPosition, Is.Not.EqualTo(initial));

            _player.Dispose();
            var disposedPlayhead = _player.Playhead;
            Assert.That(_target.transform.localPosition, Is.EqualTo(initial));

            yield return null;
            yield return null;
            Assert.That(_player.Playhead, Is.EqualTo(disposedPlayhead).Within(0.000001f));
            Assert.That(_target.transform.localPosition, Is.EqualTo(initial));
        }

        private PropertyTweener CreateTween(float duration)
        {
            return Seq.Make.TweenPosition(
                _target.transform,
                TweenStart.Absolute(Vector3.zero),
                TweenEnd.Absolute(Vector3.right * 10f),
                duration);
        }
    }
}