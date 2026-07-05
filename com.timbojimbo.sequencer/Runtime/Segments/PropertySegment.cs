using System;
using JetBrains.Annotations;
using TimboJimbo.PropertyBindings;

namespace TimboJimbo.Sequencer.Segments
{
    /// <summary>
    /// Base class for segments that drive a single <see cref="BindableProperty"/>
    /// (PropertyTweener, PropertyShaker, PropertySetter, PropertyPuncher, ...).
    ///
    /// Owns the shared plumbing so subclasses only provide their behaviour:
    ///   - StartTime / Property fields + IStartTimeConfigurable
    ///   - plan construction (timing + property binding declaration)
    ///   - the invalid-property guard, returning a NoOpPlayback
    ///   - wiring BindingCollection/Property into the created playback
    /// </summary>
    [Serializable]
    public abstract class PropertySegment : Segment, IStartTimeConfigurable, IPlaybackBuilder
    {
        public float StartTime;
        public BindableProperty Property;

        public void SetStartTime(float startTime) => StartTime = startTime;
        public float GetStartTime() => StartTime;

        /// <summary>Duration used for the plan. Duration-based subclasses override this (and typically implement IDurationConfigurable).</summary>
        public virtual float GetDuration() => 0f;

        public sealed override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
        {
            var plan = new SegmentPlan(this, parent)
            {
                Timing = { RelativeStartTime = StartTime, RelativeDuration = GetDuration() }
            };

            if (Property.IsValid)
                plan.Bindings.Properties.Add(Property);

            return plan;
        }

        public SegmentPlayback BuildPlayback(in PlaybackBuildContext context)
        {
            if (!Property.IsValid || context.PropertyBindings == null)
                return new NoOpPlayback(context);

            var playback = CreatePlayback(in context);
            if (playback == null)
                return new NoOpPlayback(context);

            playback.BindingCollection = context.PropertyBindings;
            playback.Property = Property;
            return playback;
        }

        /// <summary>
        /// Create the playback with segment-specific state. BindingCollection and Property
        /// are assigned by the base class; the property is guaranteed valid.
        /// </summary>
        protected abstract PropertyPlayback CreatePlayback(in PlaybackBuildContext context);
    }
}
