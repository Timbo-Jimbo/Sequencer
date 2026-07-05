using System;
using TimboJimbo.Core;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Builder;
using UnityEngine;

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
    public class PropertyTweener : PropertySegment, IDurationConfigurable
    {
        public float Duration;
        public EaseType Ease = EaseType.Linear;
        public TweenStart<ValueContainer> Start = TweenStart.Current<ValueContainer>();
        public TweenEnd<ValueContainer> End = TweenEnd.Initial<ValueContainer>();
        public InterpolationConfig Interpolation;
        public DiscreteValueSelectionMode DiscreteValueSelection = DiscreteValueSelectionMode.Nearest;

        /// Only meaningful when this is the earliest segment targeting its property and
        /// Start.Mode is StartFromAbsolute (there is no knowable pre-roll value otherwise).
        public PreExtrapolationMode PreExtrapolation = PreExtrapolationMode.Hold;

        public void SetDuration(float duration) => Duration = duration;
        public override float GetDuration() => Duration;

        protected override PropertyPlayback CreatePlayback(in PlaybackBuildContext context)
        {
            return new Playback(context)
            {
                Ease = Ease,
                Start = Start,
                End = End,
                Interpolation = Interpolation,
                DiscreteValueSelection = DiscreteValueSelection,
                PreExtrapolation = PreExtrapolation
            };
        }

        public class Playback : PropertyPlayback
        {
            public EaseType Ease;
            public TweenStart<ValueContainer> Start = TweenStart.Current<ValueContainer>();
            public TweenEnd<ValueContainer> End = TweenEnd.Initial<ValueContainer>();
            public InterpolationConfig Interpolation;
            public DiscreteValueSelectionMode DiscreteValueSelection;
            public PreExtrapolationMode PreExtrapolation;

            private bool _startValueInitialized;
            private bool _endValueInitialized;
            private ValueContainer _startValue;
            private ValueContainer _endValue;

            public Playback(in PlaybackBuildContext context) : base(in context)
            {
            }

            public override void Setup(in PlaybackSetupContext context)
            {
                if (End.Mode == EasedEndMode.EndAtInitial)
                {
                    var readResult = BindingCollection.TryRead(Property, out var readValue);
                    _endValue = readResult ? readValue : End.Value;
                    _endValueInitialized = true;
                }
            }

            public override bool TryGetPreExtrapolationValue(out ValueContainer value)
            {
                // Hold the absolute start value until this segment begins - otherwise
                // the property would sit at its scene value and pop when the tween starts.
                // With StartFromCurrent there is no knowable pre-roll value.
                if (PreExtrapolation == PreExtrapolationMode.Hold && Start.Mode == EasedStartMode.StartFromAbsolute)
                {
                    value = Start.Value;
                    return true;
                }

                value = default;
                return false;
            }

            public override void OnEnter(in PlaybackBoundaryContext context)
            {
                if (!_startValueInitialized)
                {
                    switch (Start.Mode)
                    {
                        case EasedStartMode.StartFromAbsolute:
                            _startValue = Start.Value;
                            break;
                        case EasedStartMode.StartFromCurrent:
                            var readSuccess = BindingCollection.TryRead(Property, out var readValue);
                            _startValue = readSuccess ? readValue : Start.Value;
                            break;
                    }

                    _startValueInitialized = true;
                }

                if (!_endValueInitialized)
                {
                    switch (End.Mode)
                    {
                        case EasedEndMode.EndAtAbsolute:
                            _endValue = End.Value;
                            break;
                        case EasedEndMode.EndAtRelative:
                            _endValue = ValueContainer.Add(_startValue, End.Value);
                            break;
                        default:
                            throw new NotImplementedException($"EndMode {End.Mode} not implemented");
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
    
    [Serializable]
    public struct TweenStart<T>
    {
        public T Value;
        public EasedStartMode Mode;
    }

    public static class TweenStart
    {
        public static TweenStart<T> Absolute<T>(T from) => new TweenStart<T> { Value = from, Mode = EasedStartMode.StartFromAbsolute };
        public static TweenStart<Vector2> Absolute(float x, float y) => new TweenStart<Vector2> { Value = new Vector2(x, y), Mode = EasedStartMode.StartFromAbsolute };
        public static TweenStart<Vector3> Absolute(float x, float y, float z) => new TweenStart<Vector3> { Value = new Vector3(x, y, z), Mode = EasedStartMode.StartFromAbsolute };
        public static TweenStart<T> Current<T>() => new TweenStart<T> { Value = default, Mode = EasedStartMode.StartFromCurrent };
    }


    [Serializable]
    public struct TweenEnd<T>
    {
        public T Value;
        public EasedEndMode Mode;
    }

    public static class TweenEnd
    {
        public static TweenEnd<T> Absolute<T>(T to) => new TweenEnd<T> { Value = to, Mode = EasedEndMode.EndAtAbsolute };
        public static TweenEnd<Vector2> Absolute(float x, float y) => new TweenEnd<Vector2> { Value = new Vector2(x, y), Mode = EasedEndMode.EndAtAbsolute };
        public static TweenEnd<Vector3> Absolute(float x, float y, float z) => new TweenEnd<Vector3> { Value = new Vector3(x, y, z), Mode = EasedEndMode.EndAtAbsolute };
        public static TweenEnd<T> Relative<T>(T to) => new TweenEnd<T> { Value = to, Mode = EasedEndMode.EndAtRelative };
        public static TweenEnd<Vector2> Relative(float x, float y) => new TweenEnd<Vector2> { Value = new Vector2(x, y), Mode = EasedEndMode.EndAtRelative };
        public static TweenEnd<Vector3> Relative(float x, float y, float z) => new TweenEnd<Vector3> { Value = new Vector3(x, y, z), Mode = EasedEndMode.EndAtRelative };
        public static TweenEnd<T> Initial<T>() => new TweenEnd<T> { Value = default, Mode = EasedEndMode.EndAtInitial };
    }

    public static class PropertyTweenerExtensions
    {
        public static PropertyTweener TweenPosition(
            this SeqMake _,
            Transform target, 
            TweenStart<Vector3> start,
            TweenEnd<Vector3> end,
            float duration, 
            EaseType ease = EaseType.Linear,
            VectorInterpolationMode interpolationMode = VectorInterpolationMode.Lerp 
        )
        {
            return new PropertyTweener
            {
                Property = BindableProperty.CreateThreeComponent(target, "m_LocalPosition", ValueKind.Vector3, "x", "y", "z"),
                Start = new TweenStart<ValueContainer> { Value = ValueContainer.FromVector3(start.Value), Mode = start.Mode },
                End = new TweenEnd<ValueContainer> { Value = ValueContainer.FromVector3(end.Value), Mode = end.Mode },
                Duration = duration,
                Ease = ease,
                Interpolation = new InterpolationConfig { Vector3 = interpolationMode }
            };
        }

        public static PropertyTweener TweenScale(
            this SeqMake _,
            Transform target, 
            TweenStart<Vector3> start,
            TweenEnd<Vector3> end,
            float duration, 
            EaseType ease = EaseType.Linear,
            VectorInterpolationMode interpolationMode = VectorInterpolationMode.Lerp 
        )
        {
            return new PropertyTweener
            {
                Property = BindableProperty.CreateThreeComponent(target, "m_LocalScale", ValueKind.Vector3, "x", "y", "z"),
                Start = new TweenStart<ValueContainer> { Value = ValueContainer.FromVector3(start.Value), Mode = start.Mode },
                End = new TweenEnd<ValueContainer> { Value = ValueContainer.FromVector3(end.Value), Mode = end.Mode },
                Duration = duration,
                Ease = ease,
                Interpolation = new InterpolationConfig { Vector3 = interpolationMode }
            };
        }

        public static PropertyTweener TweenRotation(
            this SeqMake _,
            Transform target, 
            TweenStart<Quaternion> start,
            TweenEnd<Quaternion> end,
            float duration, 
            EaseType ease = EaseType.Linear,
            RotationInterpolationMode interpolationMode = RotationInterpolationMode.QuaternionSlerp
        )
        {
            return new PropertyTweener
            {
                Property = BindableProperty.CreateFourComponent(target, "m_LocalRotation", ValueKind.Quaternion, "x", "y", "z", "w"),
                Start = new TweenStart<ValueContainer> { Value = ValueContainer.FromQuaternion(start.Value), Mode = start.Mode },
                End = new TweenEnd<ValueContainer> { Value = ValueContainer.FromQuaternion(end.Value), Mode = end.Mode },
                Duration = duration,
                Ease = ease,
                Interpolation = new InterpolationConfig { Rotation = interpolationMode }
            };
        }

        public static PropertyTweener TweenEulerRotation(
            this SeqMake _,
            Transform target, 
            TweenStart<Vector3> start,
            TweenEnd<Vector3> end,
            float duration, 
            EaseType ease = EaseType.Linear,
            RotationInterpolationMode interpolationMode = RotationInterpolationMode.EulerLerp
        )
        {
            return new PropertyTweener
            {
                Property = BindableProperty.CreateFourComponent(target, "m_LocalRotation", ValueKind.Quaternion, "x", "y", "z", "w"),
                Start = new TweenStart<ValueContainer> { Value = ValueContainer.FromQuaternion(Quaternion.Euler(start.Value)), Mode = start.Mode },
                End = new TweenEnd<ValueContainer> { Value = ValueContainer.FromQuaternion(Quaternion.Euler(end.Value)), Mode = end.Mode },
                Duration = duration,
                Ease = ease,
                Interpolation = new InterpolationConfig { Rotation = interpolationMode }
            };
        }
    }
}