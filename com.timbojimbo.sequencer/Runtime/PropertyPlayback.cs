using TimboJimbo.PropertyBindings;

namespace TimboJimbo.Sequencer
{
    /// Controls what a property-driving segment does with its property during the
    /// time BEFORE its segment starts, when it is the earliest segment targeting
    /// that property in the sequence.
    public enum PreExtrapolationMode
    {
        /// The property keeps whatever value it currently has until the segment starts.
        None,

        /// The segment's start value is written up-front (at sequence setup), so the
        /// property "holds" it until the segment starts. E.g. a canvas alpha tween
        /// 0 (absolute) -> 1 queued 5s into a sequence sits at 0 for those 5 seconds
        /// instead of popping to 0 when the tween begins.
        Hold,
    }

    /// Convenience base class for playbacks that drive a single BindableProperty
    /// (PropertyTweener, PropertyShaker, PropertySetter, PropertyPuncher, ...).
    ///
    /// Pre-extrapolation participation is not specific to this class - any playback
    /// whose segment declares its driven properties in its plan can implement
    /// <see cref="IPreExtrapolationSource"/> directly. This base simply adapts the
    /// multi-property interface to the common single-property case.
    public abstract class PropertyPlayback : SegmentPlayback, IPreExtrapolationSource
    {
        public PropertyBindingCollection BindingCollection;
        public BindableProperty Property;

        protected PropertyPlayback(in PlaybackBuildContext context) : base(in context)
        {
        }

        /// Called only when this playback is the earliest one targeting its property
        /// in the whole sequence. Return true and provide a value to have that value
        /// written up-front, so the property holds it until the segment starts.
        public virtual bool TryGetPreExtrapolationValue(out ValueContainer value)
        {
            value = default;
            return false;
        }

        bool IPreExtrapolationSource.TryGetPreExtrapolationValue(BindableProperty property, out ValueContainer value)
        {
            if (property == Property)
                return TryGetPreExtrapolationValue(out value);

            value = default;
            return false;
        }
    }

    /// Base class for playbacks that apply a strength-scaled OFFSET on top of the
    /// property's base value (PropertyShaker, PropertyPuncher, ...). Owns base-value
    /// capture/restore and additive composition; subclasses only provide the offset
    /// sampled at a normalized time.
    public abstract class OffsetPropertyPlayback : PropertyPlayback
    {
        public ValueContainer Strength;
        public bool Additive;

        private ValueContainer _baseValue;
        private bool _baseValueInitialized;
        private bool _warnedStrengthKindMismatch;

        protected OffsetPropertyPlayback(in PlaybackBuildContext context) : base(in context)
        {
            // Offsets apply on top of whatever other playbacks wrote this frame.
            ExecutionOrder = 100;
        }

        public override void Setup(in PlaybackSetupContext context)
        {
            // Re-capture the base value on every setup pass (initial values have
            // just been restored on Reset).
            _baseValueInitialized = false;
        }

        public sealed override void OnEnter(in PlaybackBoundaryContext context)
        {
            EnsureBaseValue();

            if (!Additive)
                BindingCollection.TryWrite(Property, _baseValue);
        }

        public sealed override void OnSample(in PlaybackSampleContext context)
        {
            EnsureBaseValue();

            var offsetValue = SampleOffset(context.NormalizedTime, ResolveStrengthForProperty());

            ValueContainer outputValue;
            if (Additive && BindingCollection.TryRead(Property, out var currentValue))
                outputValue = ValueContainer.Add(currentValue, offsetValue);
            else
                outputValue = ValueContainer.Add(_baseValue, offsetValue);

            BindingCollection.TryWrite(Property, outputValue);
        }

        public sealed override void OnExit(in PlaybackBoundaryContext context)
        {
            EnsureBaseValue();
            BindingCollection.TryWrite(Property, _baseValue);
        }

        /// The offset to add on top of the base/current value at the given normalized
        /// time. <paramref name="strength"/> is already resolved to the property's kind.
        protected abstract ValueContainer SampleOffset(float normalizedTime, in ValueContainer strength);

        private void EnsureBaseValue()
        {
            if (_baseValueInitialized)
                return;

            if (!BindingCollection.TryRead(Property, out _baseValue))
                _baseValue = ValueContainer.FromDefault(Property.Kind);

            _baseValueInitialized = true;
        }

        private ValueContainer ResolveStrengthForProperty()
        {
            if (Strength.Kind == Property.Kind)
                return Strength;

            if (!_warnedStrengthKindMismatch)
            {
                _warnedStrengthKindMismatch = true;
                UnityEngine.Debug.LogWarning(
                    $"{GetType().DeclaringType?.Name ?? GetType().Name}: Strength kind ({Strength.Kind}) does not match " +
                    $"property kind ({Property.Kind}) for '{Property}'. The offset will have no effect.");
            }

            return ValueContainer.FromDefault(Property.Kind);
        }
    }
}
