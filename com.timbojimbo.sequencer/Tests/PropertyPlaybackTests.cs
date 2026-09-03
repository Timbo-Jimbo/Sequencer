using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.PropertyBindings;
using TimboJimbo.PropertyBindings.Bindings;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Builder;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimboTests.Sequencer
{
    public sealed class PropertyPlaybackTests
    {
        private readonly List<SequencePlayer> _players = new();
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("Property playback target");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var player in _players)
                player.Dispose();

            Object.DestroyImmediate(_target);
        }

        [Test]
        public void AbsoluteTween_InterpolatesBetweenSuppliedValues()
        {
            var player = CreatePlayer(CreateTween(TweenStart.Absolute(Vector3.zero), TweenEnd.Absolute(Vector3.right * 10f)));

            player.Play();
            player.Tick(1f);

            AssertPositionX(5f);
        }

        [Test]
        public void CurrentTween_CapturesValueAtFirstEntry()
        {
            _target.transform.localPosition = Vector3.right * 2f;
            var player = CreatePlayer(CreateTween(TweenStart.Current<Vector3>(), TweenEnd.Absolute(Vector3.right * 10f)));

            player.Play();
            player.Tick(1f);

            AssertPositionX(6f);
        }

        [Test]
        public void RelativeTween_AddsEndOffsetToCapturedStart()
        {
            _target.transform.localPosition = Vector3.right * 2f;
            var player = CreatePlayer(CreateTween(TweenStart.Current<Vector3>(), TweenEnd.Relative(Vector3.right * 4f)));

            player.Play();
            player.Tick(1f);

            AssertPositionX(4f);
        }

        [Test]
        public void EndAtInitial_WithAbsoluteHeldStart_UsesSceneInitialAsEnd()
        {
            _target.transform.localPosition = Vector3.right * 3f;
            var tween = CreateTween(TweenStart.Absolute(Vector3.zero), TweenEnd.Initial<Vector3>());
            tween.StartTime = 1f;
            tween.PreExtrapolation = PreExtrapolationMode.Hold;
            var player = CreatePlayer(tween);

            AssertPositionX(0f);
            player.Play();
            player.Tick(2f);

            AssertPositionX(1.5f);
            player.Tick(1f);
            AssertPositionX(3f);
        }

        [Test]
        public void PreExtrapolation_HoldWritesAbsoluteStartBeforeEntry()
        {
            _target.transform.localPosition = Vector3.right * 7f;
            var tween = CreateTween(TweenStart.Absolute(Vector3.right), TweenEnd.Absolute(Vector3.right * 3f));
            tween.StartTime = 1f;
            tween.PreExtrapolation = PreExtrapolationMode.Hold;

            CreatePlayer(tween);

            AssertPositionX(1f);
        }

        [Test]
        public void PreExtrapolation_NoneLeavesSceneValueBeforeEntry()
        {
            _target.transform.localPosition = Vector3.right * 7f;
            var tween = CreateTween(TweenStart.Absolute(Vector3.right), TweenEnd.Absolute(Vector3.right * 3f));
            tween.StartTime = 1f;
            tween.PreExtrapolation = PreExtrapolationMode.None;
            var player = CreatePlayer(tween);

            player.Seek(0.5f);

            AssertPositionX(7f);
        }

        [Test]
        public void EarliestDriver_OwnsPreExtrapolation()
        {
            var sequence = new Sequence();
            var earliest = CreateTween(TweenStart.Absolute(Vector3.right), TweenEnd.Absolute(Vector3.right * 2f));
            earliest.StartTime = 1f;
            var later = CreateTween(TweenStart.Absolute(Vector3.right * 5f), TweenEnd.Absolute(Vector3.right * 6f));
            later.StartTime = 2f;
            sequence.Add(earliest);
            sequence.Add(later);

            CreatePlayer(sequence);

            AssertPositionX(1f);
        }

        [Test]
        public void EqualStartDrivers_UseDiscoveryOrderForPreExtrapolationTie()
        {
            var sequence = new Sequence();
            var first = CreateTween(TweenStart.Absolute(Vector3.right), TweenEnd.Absolute(Vector3.right * 2f));
            first.StartTime = 1f;
            var second = CreateTween(TweenStart.Absolute(Vector3.right * 5f), TweenEnd.Absolute(Vector3.right * 6f));
            second.StartTime = 1f;
            sequence.Add(first);
            sequence.Add(second);

            CreatePlayer(sequence);

            AssertPositionX(1f);
        }

        [Test]
        public void OverlappingTweens_SampleInDiscoveryOrder()
        {
            var sequence = new Sequence();
            sequence.Add(CreateTween(TweenStart.Absolute(Vector3.zero), TweenEnd.Absolute(Vector3.right * 10f)));
            sequence.Add(CreateTween(TweenStart.Absolute(Vector3.right * 10f), TweenEnd.Absolute(Vector3.right * 20f)));
            var player = CreatePlayer(sequence);

            player.Play();
            player.Tick(1f);

            AssertPositionX(15f);
        }

        [TestCase(true, 3f)]
        [TestCase(false, 0.5f)]
        public void Punch_AdditiveModeControlsCompositionWithTween(bool additive, float expected)
        {
            var sequence = new Sequence();
            sequence.Add(CreateTween(TweenStart.Absolute(Vector3.zero), TweenEnd.Absolute(Vector3.right * 10f)));
            var punch = Seq.Make.PunchPosition(_target.transform, Vector3.right, 1f, vibrato: 1, elasticity: 1f);
            punch.Additive = additive;
            sequence.Add(punch);
            var player = CreatePlayer(sequence);

            player.Play();
            player.Tick(0.5f);

            AssertPositionX(expected, 0.001f);
        }

        [Test]
        public void Punch_RestoresCapturedBaseValueOnExit()
        {
            _target.transform.localPosition = Vector3.right * 4f;
            var player = CreatePlayer(Seq.Make.PunchPosition(_target.transform, Vector3.right, 1f, vibrato: 1, elasticity: 1f));

            player.Play();
            player.Tick(1f);

            AssertPositionX(4f);
        }

        [Test]
        public void Reset_RestoresInitialValueAndReplaysDeterministically()
        {
            _target.transform.localPosition = Vector3.right * 2f;
            var player = CreatePlayer(CreateTween(TweenStart.Current<Vector3>(), TweenEnd.Absolute(Vector3.right * 10f)));
            player.Play();
            player.Tick(1f);
            AssertPositionX(6f);

            player.Stop();
            AssertPositionX(2f);
            player.Play();
            player.Tick(1f);

            AssertPositionX(6f);
        }

        [Test]
        public void MalformedSetterValueKind_DoesNotModifyProperty()
        {
            _target.transform.localPosition = Vector3.right * 4f;
            var setter = Seq.Make.SetPosition(_target.transform, Vector3.right * 10f);
            setter.StartTime = 0.5f;
            setter.Value = ValueContainer.FromFloat(99f);
            setter.PreExtrapolation = PreExtrapolationMode.None;
            var player = CreatePlayer(setter);

            player.Play();
            player.Tick(0.5f);

            AssertPositionX(4f);
        }

        [Test]
        public void GenericFactories_TweenNonTransformDescriptorEndToEnd()
        {
            var group = _target.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            var tween = Seq.Make.Tween(group, CanvasGroupProperties.Alpha,
                TweenStart.Absolute(0f), TweenEnd.Absolute(1f), 2f);
            var player = CreatePlayer(tween);

            player.Play();
            player.Tick(1f);

            Assert.That(group.alpha, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(Seq.Make.Set(group, CanvasGroupProperties.Alpha, 0.25f).Value.FloatValue, Is.EqualTo(0.25f));
        }

        [Test]
        public void DescriptorBackedTransformProperty_SelectsSpecializedBinding()
        {
            var setter = Seq.Make.SetPosition(_target.transform, Vector3.one);

            Assert.That(setter.Property.HasDescriptor, Is.True);
            var report = PropertyBindingRegistry.Diagnose(_target, setter.Property);
            Assert.That(report.SelectedBindingType, Is.EqualTo(typeof(TransformPropertyBinding)));
        }

        private PropertyTweener CreateTween(TweenStart<Vector3> start, TweenEnd<Vector3> end)
        {
            return Seq.Make.TweenPosition(_target.transform, start, end, 2f);
        }

        private SequencePlayer CreatePlayer(Segment segment)
        {
            var player = segment.CreatePlayer();
            _players.Add(player);
            return player;
        }

        private void AssertPositionX(float expected, float tolerance = 0.0001f)
        {
            Assert.That(_target.transform.localPosition.x, Is.EqualTo(expected).Within(tolerance));
        }
    }
}