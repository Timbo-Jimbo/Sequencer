using TimboJimbo.PropertyBindings;

namespace TimboJimbo.Sequencer
{
    /// Base class for playbacks that drive a single BindableProperty
    /// (PropertyTweener, PropertyShaker, PropertySetter, PropertyPuncher, ...).
    ///
    /// All PropertyPlaybacks in a sequence coordinate with each other: during Setup,
    /// the playback with the earliest AbsoluteStartTime for a given property receives
    /// an InitializeProperty callback, allowing it to establish the property's initial
    /// value (e.g. a tweener with an absolute start value writes it up-front to avoid
    /// a pop when its segment begins).
    public abstract class PropertyPlayback : SegmentPlayback
    {
        public PropertyBindingCollection BindingCollection;
        public BindableProperty Property;

        protected PropertyPlayback(in PlaybackBuildContext context) : base(in context)
        {
        }

        public sealed override void Setup(in PlaybackSetupContext context)
        {
            OnSetup(in context);

            if (Property.IsValid && IsFirstPlaybackForProperty(in context))
                InitializeProperty(in context);
        }

        /// Regular per-playback setup work. Replaces overriding Setup directly.
        protected virtual void OnSetup(in PlaybackSetupContext context) { }

        /// Called during Setup, only on the playback that is the FIRST (earliest
        /// AbsoluteStartTime) to target this property in the whole sequence.
        /// Use it to establish the property's initial value.
        protected virtual void InitializeProperty(in PlaybackSetupContext context) { }

        /// True if this playback is the earliest one targeting its property.
        /// Ties are broken by playback list order.
        public bool IsFirstPlaybackForProperty(in PlaybackSetupContext context)
        {
            PropertyPlayback earliest = null;
            foreach (var playback in context.Playbacks)
            {
                if (playback is PropertyPlayback other
                    && other.Property == Property
                    && (earliest == null || other.AbsoluteStartTime < earliest.AbsoluteStartTime))
                {
                    earliest = other;
                }
            }

            return ReferenceEquals(earliest, this);
        }
    }
}
