using TimboJimbo.Core;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimbo.Sequencer.Builder
{
    /// <summary>
    /// Descriptor-based property segment factories. Any <see cref="PropertyDescriptor{TTarget,TValue}"/>
    /// (built-in or user-defined) works here; the typed value parameters make kind mismatches a
    /// compile error. The Transform/RectTransform convenience helpers are thin wrappers over these.
    /// </summary>
    public static class PropertySegmentFactories
    {
        public static PropertyTweener Tween<TTarget, TValue>(
            this SeqMake _,
            TTarget target,
            PropertyDescriptor<TTarget, TValue> descriptor,
            TweenStart<TValue> start,
            TweenEnd<TValue> end,
            float duration,
            EaseType ease = EaseType.Linear,
            InterpolationConfig interpolation = default
        ) where TTarget : Object
        {
            return new PropertyTweener
            {
                Property = BindableProperty.Create(target, descriptor),
                Start = new TweenStart<ValueContainer> { Value = ValueContainer.From(start.Value), Mode = start.Mode },
                End = new TweenEnd<ValueContainer> { Value = ValueContainer.From(end.Value), Mode = end.Mode },
                Duration = duration,
                Ease = ease,
                Interpolation = interpolation
            };
        }

        public static PropertySetter Set<TTarget, TValue>(
            this SeqMake _,
            TTarget target,
            PropertyDescriptor<TTarget, TValue> descriptor,
            TValue value
        ) where TTarget : Object
        {
            return new PropertySetter
            {
                Property = BindableProperty.Create(target, descriptor),
                Value = ValueContainer.From(value)
            };
        }

        public static PropertyPuncher Punch<TTarget, TValue>(
            this SeqMake _,
            TTarget target,
            PropertyDescriptor<TTarget, TValue> descriptor,
            TValue strength,
            float duration,
            int vibrato = 10,
            float elasticity = 1f
        ) where TTarget : Object
        {
            return new PropertyPuncher
            {
                Property = BindableProperty.Create(target, descriptor),
                Strength = ValueContainer.From(strength),
                Duration = duration,
                Vibrato = vibrato,
                Elasticity = elasticity,
                Additive = true
            };
        }

        public static PropertyShaker Shake<TTarget, TValue>(
            this SeqMake _,
            TTarget target,
            PropertyDescriptor<TTarget, TValue> descriptor,
            TValue strength,
            float duration,
            int vibrato = 10,
            float randomness = 90f
        ) where TTarget : Object
        {
            return new PropertyShaker
            {
                Property = BindableProperty.Create(target, descriptor),
                Strength = ValueContainer.From(strength),
                Duration = duration,
                Vibrato = vibrato,
                Randomness = randomness,
                Additive = true
            };
        }
    }
}
