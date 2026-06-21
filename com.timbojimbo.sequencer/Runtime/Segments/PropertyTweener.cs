using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using TimboJimbo.Core;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Builder;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Events;

namespace TimboJimbo.Sequencer.Segments
{
    public enum EasedStartMode
    {
        StartFromAbsolute,
        StartFromCurrent
    }

    public enum EasedEndMode
    {
        EndAtAbsolute,
        EndAtRelative,
        EndAtInitial
    }

    [Serializable]
    [AddSegmentMenu("")]
    public class PropertyTweener : Segment, IStartTimeConfigurable, IDurationConfigurable, IPlaybackBuilder
    {
        public float StartTime;
        public float Duration;
        public BindableProperty Property;
        public EaseType Ease = EaseType.Linear;
        public EasedStartMode StartMode = EasedStartMode.StartFromCurrent;
        public EasedEndMode EndMode = EasedEndMode.EndAtAbsolute;
        public ValueContainer StartValue;
        public ValueContainer EndValue;
        public InterpolationConfig Interpolation;
        public DiscreteValueSelectionMode DiscreteValueSelection = DiscreteValueSelectionMode.Nearest;

        public void SetDuration(float duration) => Duration = duration;
        public float GetDuration() => Duration;
        public void SetStartTime(float startTime) => StartTime = startTime;
        public float GetStartTime() => StartTime;

        public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
        {
            var blueprint = new SegmentPlan(this, parent)
            {
                Bindings = { Properties = new HashSet<BindableProperty> { Property } },
                Timing = { RelativeStartTime = StartTime, RelativeDuration = Duration }
            };

            return blueprint;
        }

        public SegmentPlayback BuildPlayback(in PlaybackBuildContext context)
        {
            return new Playback(context)
            {
                BindingCollection = context.PropertyBindings,
                Property = Property,
                Ease = Ease,
                StartMode = StartMode,
                EndMode = EndMode,
                StartValue = StartValue,
                EndValue = EndValue,
                Interpolation = Interpolation,
                DiscreteValueSelection = DiscreteValueSelection
            };
        }

        public class Playback : SegmentPlayback
        {
            public PropertyBindingCollection BindingCollection;
            public BindableProperty Property;
            public EaseType Ease;
            public EasedStartMode StartMode;
            public EasedEndMode EndMode;
            public ValueContainer StartValue;
            public ValueContainer EndValue;
            public InterpolationConfig Interpolation;
            public DiscreteValueSelectionMode DiscreteValueSelection;

            private bool _startValueInitialized;
            private bool _endValueInitialized;
            private ValueContainer _startValue;
            private ValueContainer _endValue;

            public Playback(in PlaybackBuildContext context) : base(in context)
            {
            }

            public override void Setup(in PlaybackSetupContext context)
            {
                if (EndMode == EasedEndMode.EndAtInitial)
                {
                    var readResult = BindingCollection.TryRead(Property, out var readValue);
                    _endValue = readResult ? readValue : EndValue;
                    _endValueInitialized = true;
                }

                if(StartMode == EasedStartMode.StartFromAbsolute)
                {
                    //are we the first segment to write to this property?
                    var isFirst = true;
                    foreach (var playback in context.Sequence.AllPlaybacks)
                    {
                        if (playback == this)
                            break;

                        if (playback is Playback p && p.Property == Property)
                        {
                            isFirst = false;
                            break;
                        }
                    }

                    if (isFirst)
                    {
                        // if so, we need to ensure the start value is correct from the outset
                        //otherwise we will get a pop at the start of this segment.
                        BindingCollection.TryWrite(Property, StartValue);
                    }
                }
            }

            public override void OnEnter(in PlaybackBoundaryContext context)
            {
                if (!_startValueInitialized)
                {
                    switch (StartMode)
                    {
                        case EasedStartMode.StartFromAbsolute:
                            _startValue = StartValue;
                            break;
                        case EasedStartMode.StartFromCurrent:
                            var readSuccess = BindingCollection.TryRead(Property, out var readValue);
                            _startValue = readSuccess ? readValue : StartValue;
                            break;
                    }

                    _startValueInitialized = true;
                }

                if (!_endValueInitialized)
                {
                    switch (EndMode)
                    {
                        case EasedEndMode.EndAtAbsolute:
                            _endValue = EndValue;
                            break;
                        case EasedEndMode.EndAtRelative:
                            _endValue = ValueContainer.Add(_startValue, EndValue);
                            break;
                        default:
                            throw new NotImplementedException($"EndMode {EndMode} not implemented");
                    }

                    _endValueInitialized = true;
                }
            }

            public override void OnSample(in PlaybackSampleContext context)
            {
                var easedT = EaseUtility.Evaluate(context.NormalizedTime, Ease);
                var resultValue = ValueContainer.LerpUnclamped(
                    _startValue,
                    _endValue,
                    easedT,
                    Interpolation,
                    DiscreteValueSelection);
                BindingCollection.TryWrite(Property, resultValue);
            }

            public override void OnExit(in PlaybackBoundaryContext context)
            {
                BindingCollection.TryWrite(Property, _endValue);

            }
        }
    }
    public struct TweenStart<T>
    {
        public T Value;
        public EasedStartMode StartMode;
    }

    public static class TweenStart
    {
        public static TweenStart<T> Absolute<T>(T from) => new TweenStart<T> { Value = from, StartMode = EasedStartMode.StartFromAbsolute };
        public static TweenStart<Vector2> Absolute(float x, float y) => new TweenStart<Vector2> { Value = new Vector2(x, y), StartMode = EasedStartMode.StartFromAbsolute };
        public static TweenStart<Vector3> Absolute(float x, float y, float z) => new TweenStart<Vector3> { Value = new Vector3(x, y, z), StartMode = EasedStartMode.StartFromAbsolute };
        public static TweenStart<T> Current<T>() => new TweenStart<T> { Value = default, StartMode = EasedStartMode.StartFromCurrent };
    }


    public struct TweenEnd<T>
    {
        public T Value;
        public EasedEndMode EndMode;
    }

    public static class TweenEnd
    {
        public static TweenEnd<T> Absolute<T>(T to) => new TweenEnd<T> { Value = to, EndMode = EasedEndMode.EndAtAbsolute };
        public static TweenEnd<Vector2> Absolute(float x, float y) => new TweenEnd<Vector2> { Value = new Vector2(x, y), EndMode = EasedEndMode.EndAtAbsolute };
        public static TweenEnd<Vector3> Absolute(float x, float y, float z) => new TweenEnd<Vector3> { Value = new Vector3(x, y, z), EndMode = EasedEndMode.EndAtAbsolute };
        public static TweenEnd<T> Relative<T>(T to) => new TweenEnd<T> { Value = to, EndMode = EasedEndMode.EndAtRelative };
        public static TweenEnd<Vector2> Relative(float x, float y) => new TweenEnd<Vector2> { Value = new Vector2(x, y), EndMode = EasedEndMode.EndAtRelative };
        public static TweenEnd<Vector3> Relative(float x, float y, float z) => new TweenEnd<Vector3> { Value = new Vector3(x, y, z), EndMode = EasedEndMode.EndAtRelative };
        public static TweenEnd<T> Initial<T>() => new TweenEnd<T> { Value = default, EndMode = EasedEndMode.EndAtInitial };
    }

    public struct TweenGroup
    {
        public GameObject BindingRoot;
    }

    public static class PropertyTweenerExtensions
    {
        public static Segment TweenGroup(
            this SegMake _,
            GameObject withBindingRoot,
            Func<TweenGroup, Segment> groupContent
        )
        {
            Assert.IsTrue(withBindingRoot != null, "Tween group's BindingRoot must be set");

            var group = new TweenGroup() { BindingRoot = withBindingRoot };
            var contentSegment = groupContent(group);
            
            return new Sequence()
            {
                BindingRoot = withBindingRoot,
                Segments = { contentSegment },
            };
        }

        public static PropertyTweener Position(
            this TweenGroup _,
            Transform target, 
            TweenStart<Vector3> start,
            TweenEnd<Vector3> end,
            float duration, 
            EaseType ease = EaseType.Linear,
            VectorInterpolationMode interpolationMode = VectorInterpolationMode.Lerp 
        )
        {
            Assert.IsTrue(_.BindingRoot != null, "Tween group's BindingRoot must be set");
            Assert.IsTrue(target.IsChildOf(_.BindingRoot.transform), "Target must be a child of the tween group's BindingRoot");
            
            return new PropertyTweener
            {
                Property = BindableProperty.CreateThreeComponent(target, "m_LocalPosition", ValueKind.Vector3, "x", "y", "z"),
                StartValue = ValueContainer.FromVector3(start.Value),
                EndValue = ValueContainer.FromVector3(end.Value),
                Duration = duration,
                Ease = ease,
                StartMode = start.StartMode,
                EndMode = end.EndMode,
                Interpolation = new InterpolationConfig { Vector3 = interpolationMode }
            };
        }

        public static PropertyTweener Scale(
            this TweenGroup _,
            Transform target, 
            TweenStart<Vector3> start,
            TweenEnd<Vector3> end,
            float duration, 
            EaseType ease = EaseType.Linear,
            VectorInterpolationMode interpolationMode = VectorInterpolationMode.Lerp 
        )
        {
            Assert.IsTrue(_.BindingRoot != null, "Tween group's BindingRoot must be set");
            Assert.IsTrue(target.IsChildOf(_.BindingRoot.transform), "Target must be a child of the tween group's BindingRoot");

            return new PropertyTweener
            {
                Property = BindableProperty.CreateThreeComponent(target, "m_LocalScale", ValueKind.Vector3, "x", "y", "z"),
                StartValue = ValueContainer.FromVector3(start.Value),
                EndValue = ValueContainer.FromVector3(end.Value),
                Duration = duration,
                Ease = ease,
                StartMode = start.StartMode,
                EndMode = end.EndMode,
                Interpolation = new InterpolationConfig { Vector3 = interpolationMode }
            };
        }

        public static PropertyTweener Rotation(
            this TweenGroup _,
            Transform target, 
            TweenStart<Quaternion> start,
            TweenEnd<Quaternion> end,
            float duration, 
            EaseType ease = EaseType.Linear,
            RotationInterpolationMode interpolationMode = RotationInterpolationMode.QuaternionSlerp
        )
        {
            Assert.IsTrue(_.BindingRoot != null, "Tween group's BindingRoot must be set");
            Assert.IsTrue(target.IsChildOf(_.BindingRoot.transform), "Target must be a child of the tween group's BindingRoot");

            return new PropertyTweener
            {
                Property = BindableProperty.CreateFourComponent(target, "m_LocalRotation", ValueKind.Quaternion, "x", "y", "z", "w"),
                StartValue = ValueContainer.FromQuaternion(start.Value),
                EndValue = ValueContainer.FromQuaternion(end.Value),
                Duration = duration,
                Ease = ease,
                StartMode = start.StartMode,
                EndMode = end.EndMode,
                Interpolation = new InterpolationConfig { Rotation = interpolationMode }
            };
        }

        public static PropertyTweener EulerRotation(
            this TweenGroup _,
            Transform target, 
            TweenStart<Vector3> start,
            TweenEnd<Vector3> end,
            float duration, 
            EaseType ease = EaseType.Linear,
            RotationInterpolationMode interpolationMode = RotationInterpolationMode.EulerLerp
        )
        {
            Assert.IsTrue(_.BindingRoot != null, "Tween group's BindingRoot must be set");
            Assert.IsTrue(target.IsChildOf(_.BindingRoot.transform), "Target must be a child of the tween group's BindingRoot");

            return new PropertyTweener
            {
                Property = BindableProperty.CreateFourComponent(target, "m_LocalRotation", ValueKind.Quaternion, "x", "y", "z", "w"),
                StartValue = ValueContainer.FromQuaternion(Quaternion.Euler(start.Value)),
                EndValue = ValueContainer.FromQuaternion(Quaternion.Euler(end.Value)),
                Duration = duration,
                Ease = ease,
                StartMode = start.StartMode,
                EndMode = end.EndMode,
                Interpolation = new InterpolationConfig { Rotation = interpolationMode }
            };
        }

        // This isn't a property tweener, but will probably be used in conjunction with them
        // so we may as well surface it as part of PropetyTweenGroup extensions
        public static CustomTweener Custom(
            this TweenGroup _,
            UnityAction<float> onSample = null, 
            float duration = 1f, 
            EaseType ease = EaseType.Linear
        )
        {
            var customTweener = new CustomTweener
            {
                Duration = duration,
                Ease = ease,
                OnSample = new UnityEvent<float>()
            };

            if (onSample != null)
                customTweener.OnSample.AddListener(onSample);

            return customTweener;
        }

        public static CustomTweener Custom(
            this TweenGroup _,
            UnityAction<float, CustomTweenerSampleContext> onSample = null, 
            float duration = 1f, 
            EaseType ease = EaseType.Linear
        )
        {
            var customTweener = new CustomTweener
            {
                Duration = duration,
                Ease = ease,
                OnSampleWithContext = new UnityEvent<float, CustomTweenerSampleContext>()
            };

            if (onSample != null)
                customTweener.OnSampleWithContext.AddListener(onSample);

            return customTweener;
        }
    }
}