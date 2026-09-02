using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Builder;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TimboJimboTests.Sequencer
{
    public sealed class SequenceCompilationTests
    {
        private readonly List<GameObject> _gameObjects = new();
        private GameObject _target;
        private UnresolvableTarget _unresolvableTarget;

        [SetUp]
        public void SetUp()
        {
            _target = CreateGameObject("Sequencer test target");
            _unresolvableTarget = ScriptableObject.CreateInstance<UnresolvableTarget>();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _gameObjects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_gameObjects[i]);

            Object.DestroyImmediate(_unresolvableTarget);
        }

        [Test]
        public void CreatePlayer_WithOnlyUnresolvablePropertyTarget_ReturnsUsablePlayer()
        {
            var sequence = new Sequence();
            sequence.Add(CreateUnresolvableSetter());

            ExpectUnresolvableTargetWarning();

            using var player = SequencePlayer.Create(sequence);

            Assert.That(player, Is.Not.Null);
            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Idle));
        }

        [Test]
        public void CreatePlayer_WithUnresolvablePropertyTarget_StillPlaysValidSibling()
        {
            var validSetter = Seq.Make.SetPosition(_target.transform, Vector3.one);
            validSetter.StartTime = 0.5f;
            validSetter.PreExtrapolation = PreExtrapolationMode.None;

            var sequence = new Sequence();
            sequence.Add(CreateUnresolvableSetter());
            sequence.Add(validSetter);

            ExpectUnresolvableTargetWarning();

            using var player = SequencePlayer.Create(sequence);
            player.Play();
            player.Tick(0.5f);

            Assert.That(_target.transform.localPosition, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void NestedSequence_PropagatesAbsoluteTimingAndDuration()
        {
            var inner = new Sequence { StartTime = 1f };
            inner.Add(new TestDurationSegment { Duration = 2f });
            var outer = new Sequence { StartTime = 3f };
            outer.Add(inner);

            var plan = outer.GetPlan(null);

            Assert.That(plan.Timing.AbsoluteStartTime, Is.EqualTo(3f));
            Assert.That(plan.Timing.AbsoluteDuration, Is.EqualTo(3f));
            Assert.That(plan.Children[0].Timing.AbsoluteStartTime, Is.EqualTo(4f));
            Assert.That(plan.Children[0].Children[0].Timing.AbsoluteStartTime, Is.EqualTo(4f));
        }

        [Test]
        public void Together_StartsAllChildrenAtZeroAndUsesLongestDuration()
        {
            var segment = Seq.Schedule.Together(
                new TestDurationSegment { Duration = 1f },
                new TestDurationSegment { Duration = 2f });

            var plan = segment.GetPlan(null);

            Assert.That(plan.Children[0].Timing.RelativeStartTime, Is.Zero);
            Assert.That(plan.Children[1].Timing.RelativeStartTime, Is.Zero);
            Assert.That(plan.Timing.RelativeDuration, Is.EqualTo(2f));
        }

        [Test]
        public void OneAfterAnother_StartsEachChildAtPreviousEnd()
        {
            var segment = Seq.Schedule.OneAfterAnother(
                new TestDurationSegment { Duration = 1f },
                new TestDurationSegment { Duration = 2f });

            var plan = segment.GetPlan(null);

            Assert.That(plan.Children[0].Timing.RelativeStartTime, Is.Zero);
            Assert.That(plan.Children[1].Timing.RelativeStartTime, Is.EqualTo(1f));
            Assert.That(plan.Timing.RelativeDuration, Is.EqualTo(3f));
        }

        [Test]
        public void Stagger_UsesFixedIndexDelay()
        {
            var segment = Seq.Schedule.Stagger(0.5f,
                new TestDurationSegment { Duration = 1f },
                new TestDurationSegment { Duration = 1f });

            var plan = segment.GetPlan(null);

            Assert.That(plan.Children[0].Timing.RelativeStartTime, Is.Zero);
            Assert.That(plan.Children[1].Timing.RelativeStartTime, Is.EqualTo(0.5f));
            Assert.That(plan.Timing.RelativeDuration, Is.EqualTo(1.5f));
        }

        [Test]
        public void ProportionalStagger_UsesPreviousStartPlusDurationFraction()
        {
            var segment = Seq.Schedule.ProportionalStagger(
                new TestDurationSegment { Duration = 2f },
                new TestDurationSegment { Duration = 4f },
                percent01: 0.25f);

            var plan = segment.GetPlan(null);

            Assert.That(plan.Children[1].Timing.RelativeStartTime, Is.EqualTo(0.5f));
            Assert.That(plan.Timing.RelativeDuration, Is.EqualTo(4.5f));
        }

        [Test]
        public void Wait_OffsetsFollowingSegment()
        {
            var segment = Seq.Schedule.Wait(1.5f, new TestDurationSegment { Duration = 2f });

            var plan = segment.GetPlan(null);

            Assert.That(plan.Children[0].Timing.RelativeStartTime, Is.EqualTo(1.5f));
            Assert.That(plan.Timing.RelativeDuration, Is.EqualTo(3.5f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void CustomArrangement_NonFiniteOutputFallsBackToIntrinsicStart(float invalidStart)
        {
            var child = new Sequence { StartTime = 0.75f };
            child.Add(new TestDurationSegment { Duration = 1f });
            var segment = Seq.Schedule.CustomArrangement(_ => invalidStart, new[] { child });

            var plan = segment.GetPlan(null);

            Assert.That(plan.Children[0].Timing.RelativeStartTime, Is.EqualTo(0.75f));
        }

        [Test]
        public void Conditional_CompilesOnlySelectedBranch()
        {
            var segment = Seq.Schedule.If(false,
                new TestDurationSegment { Duration = 1f },
                new TestDurationSegment { Duration = 3f });

            var plan = segment.GetPlan(null);

            Assert.That(plan.Children, Has.Count.EqualTo(1));
            Assert.That(plan.Timing.RelativeDuration, Is.EqualTo(3f));
        }

        [Test]
        public void InvalidProperty_CompilesAsHarmlessNoOp()
        {
            var setter = new PropertySetter { Property = BindableProperty.Invalid, StartTime = 0.5f };

            using var player = setter.CreatePlayer();
            player.Play();

            Assert.DoesNotThrow(() => player.Tick(0.5f));
            Assert.That(player.State, Is.EqualTo(SequencePlaybackState.Completed));
        }

        [Test]
        public void BindingRootAbsorption_PreservesPropertiesOnParentAndChildHierarchies()
        {
            var parent = CreateGameObject("Binding parent");
            var child = CreateGameObject("Binding child");
            child.transform.SetParent(parent.transform);
            var sequence = new Sequence();
            var childSetter = Seq.Make.SetPosition(child.transform, Vector3.one);
            childSetter.StartTime = 0.5f;
            childSetter.PreExtrapolation = PreExtrapolationMode.None;
            var parentSetter = Seq.Make.SetScale(parent.transform, Vector3.one * 2f);
            parentSetter.StartTime = 0.5f;
            parentSetter.PreExtrapolation = PreExtrapolationMode.None;
            sequence.Add(childSetter);
            sequence.Add(parentSetter);

            using var player = sequence.CreatePlayer();
            player.Play();
            player.Tick(0.5f);

            Assert.That(child.transform.localPosition, Is.EqualTo(Vector3.one));
            Assert.That(parent.transform.localScale, Is.EqualTo(Vector3.one * 2f));
        }

        [Test]
        public void InsertSequenceProvider_CompilesAndPlaysIncludedSequence()
        {
            var providerObject = CreateGameObject("Included provider");
            var provider = providerObject.AddComponent<SequenceProvider>();
            var includedSetter = Seq.Make.SetPosition(_target.transform, Vector3.one * 3f);
            includedSetter.StartTime = 0.5f;
            includedSetter.PreExtrapolation = PreExtrapolationMode.None;
            provider.UpsertSequence("Included", new Segment[] { includedSetter });
            var include = new InsertSequenceProvider
            {
                Provider = provider,
                SequenceName = "Included",
                StartTime = 0.5f
            };

            using var player = include.CreatePlayer();
            player.Play();
            player.Tick(1f);

            Assert.That(_target.transform.localPosition, Is.EqualTo(Vector3.one * 3f));
        }

        [Test]
        public void PlanConstructionFailure_PropagatesOriginalException()
        {
            var segment = new ThrowingPlanSegment();

            var exception = Assert.Throws<InvalidOperationException>(() => segment.CreatePlayer());

            Assert.That(exception.Message, Is.EqualTo(ThrowingPlanSegment.FailureMessage));
        }

        [Test]
        public void CustomPlaybackBuilder_ExecutesBoundaryHooks()
        {
            var segment = new RecordingPlaybackSegment { StartTime = 0.5f, Duration = 0.5f };

            using var player = segment.CreatePlayer();
            player.Play();
            player.Tick(1f);

            Assert.That(segment.EnterCount, Is.EqualTo(1));
            Assert.That(segment.ExitCount, Is.EqualTo(1));
        }

        [Test]
        public void CustomPreExtrapolationSource_WritesDeclaredPropertyDuringSetup()
        {
            var property = BindableProperty.Create(_target.transform, TransformProperties.LocalPosition);
            var segment = new PreExtrapolatingSegment
            {
                Property = property,
                Value = ValueContainer.FromVector3(Vector3.one * 4f),
                StartTime = 1f
            };

            using var player = segment.CreatePlayer();

            Assert.That(_target.transform.localPosition, Is.EqualTo(Vector3.one * 4f));
        }

        private PropertySetter CreateUnresolvableSetter()
        {
            return new PropertySetter
            {
                Property = BindableProperty.CreateAdHoc(_unresolvableTarget, nameof(UnresolvableTarget.Value), ValueKind.Float),
                Value = ValueContainer.FromFloat(1f)
            };
        }

        private static void ExpectUnresolvableTargetWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("non-GameObject related target.*ignored"));
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            _gameObjects.Add(gameObject);
            return gameObject;
        }
    }

    internal sealed class UnresolvableTarget : ScriptableObject
    {
        public float Value;
    }

    internal sealed class ThrowingPlanSegment : Segment
    {
        public const string FailureMessage = "Expected plan construction failure.";

        public override SegmentPlan GetPlan(SegmentPlan parent)
        {
            throw new InvalidOperationException(FailureMessage);
        }
    }

    internal sealed class RecordingPlaybackSegment : Segment, IPlaybackBuilder
    {
        public float StartTime;
        public float Duration;
        public int EnterCount;
        public int ExitCount;

        public override SegmentPlan GetPlan(SegmentPlan parent)
        {
            return new SegmentPlan(this, parent)
            {
                Timing = { RelativeStartTime = StartTime, RelativeDuration = Duration }
            };
        }

        public SegmentPlayback BuildPlayback(in PlaybackBuildContext context)
        {
            return new Playback(in context, this);
        }

        private sealed class Playback : SegmentPlayback
        {
            private readonly RecordingPlaybackSegment _owner;

            public Playback(in PlaybackBuildContext context, RecordingPlaybackSegment owner) : base(in context)
            {
                _owner = owner;
            }

            public override void OnEnter(in PlaybackBoundaryContext context) => _owner.EnterCount++;
            public override void OnExit(in PlaybackBoundaryContext context) => _owner.ExitCount++;
        }
    }

    internal sealed class PreExtrapolatingSegment : Segment, IPlaybackBuilder
    {
        public BindableProperty Property;
        public ValueContainer Value;
        public float StartTime;

        public override SegmentPlan GetPlan(SegmentPlan parent)
        {
            var plan = new SegmentPlan(this, parent)
            {
                Timing = { RelativeStartTime = StartTime }
            };
            plan.Bindings.Properties.Add(Property);
            return plan;
        }

        public SegmentPlayback BuildPlayback(in PlaybackBuildContext context)
        {
            return new Playback(in context, Property, Value);
        }

        private sealed class Playback : SegmentPlayback, IPreExtrapolationSource
        {
            private readonly BindableProperty _property;
            private readonly ValueContainer _value;

            public Playback(in PlaybackBuildContext context, BindableProperty property, ValueContainer value) : base(in context)
            {
                _property = property;
                _value = value;
            }

            public bool TryGetPreExtrapolationValue(BindableProperty property, out ValueContainer value)
            {
                value = _value;
                return property == _property;
            }
        }
    }
}