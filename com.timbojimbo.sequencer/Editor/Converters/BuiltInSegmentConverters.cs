using System.Collections.Generic;
using System.Linq;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimboEditor.Sequencer.Converters
{
    /// Shared logic for converting between the "property family" segments:
    /// PropertyTweener, PropertyShaker, PropertySetter and PropertyPuncher.
    public abstract class PropertyFamilySegmentConverter : SegmentConverter
    {
        private const float DefaultDuration = 0.5f;

        protected abstract System.Type TargetType { get; }

        public override bool CanConvert(IReadOnlyList<Segment> segments)
        {
            if (segments == null || segments.Count != 1)
                return false;

            var segment = segments[0];
            if (segment == null || segment.GetType() == TargetType)
                return false;

            return segment is PropertyTweener or PropertyShaker or PropertySetter or PropertyPuncher;
        }

        public override List<Segment> Convert(IReadOnlyList<Segment> segments)
        {
            if (!TryExtract(segments[0], out var data))
                return null;

            var converted = CreateFrom(data);
            return converted != null ? new List<Segment> { converted } : null;
        }

        protected abstract Segment CreateFrom(in CommonData data);

        protected struct CommonData
        {
            public float StartTime;
            public float Duration;
            public BindableProperty Property;
            public ValueContainer Value;
        }

        private static bool TryExtract(Segment segment, out CommonData data)
        {
            if (segment is not PropertySegment propertySegment)
            {
                data = default;
                return false;
            }

            data = new CommonData
            {
                StartTime = propertySegment.StartTime,
                Duration = propertySegment.GetDuration(),
                Property = propertySegment.Property,
                // Only the "defining" value differs per segment type.
                Value = segment switch
                {
                    PropertyTweener tweener => tweener.End.Value,
                    PropertyShaker shaker => shaker.Strength,
                    PropertyPuncher puncher => puncher.Strength,
                    PropertySetter setter => setter.Value,
                    _ => default
                }
            };
            return true;
        }

        protected static float EnsureDuration(float duration) => duration > 0f ? duration : DefaultDuration;
    }

    [SegmentConverter]
    public sealed class ToPropertyTweenerConverter : PropertyFamilySegmentConverter
    {
        public override string MenuName => "Property Tweener";
        protected override System.Type TargetType => typeof(PropertyTweener);

        protected override Segment CreateFrom(in CommonData data)
        {
            return new PropertyTweener
            {
                StartTime = data.StartTime,
                Duration = EnsureDuration(data.Duration),
                Property = data.Property,
                Start = TweenStart.Current<ValueContainer>(),
                End = TweenEnd.Absolute(data.Value),
            };
        }
    }

    [SegmentConverter]
    public sealed class ToPropertyShakerConverter : PropertyFamilySegmentConverter
    {
        public override string MenuName => "Property Shaker";
        protected override System.Type TargetType => typeof(PropertyShaker);

        protected override Segment CreateFrom(in CommonData data)
        {
            return new PropertyShaker
            {
                StartTime = data.StartTime,
                Duration = EnsureDuration(data.Duration),
                Property = data.Property,
                Strength = data.Value,
            };
        }
    }

    [SegmentConverter]
    public sealed class ToPropertyPuncherConverter : PropertyFamilySegmentConverter
    {
        public override string MenuName => "Property Puncher";
        protected override System.Type TargetType => typeof(PropertyPuncher);

        protected override Segment CreateFrom(in CommonData data)
        {
            return new PropertyPuncher
            {
                StartTime = data.StartTime,
                Duration = EnsureDuration(data.Duration),
                Property = data.Property,
                Strength = data.Value,
            };
        }
    }

    [SegmentConverter]
    public sealed class ToPropertySetterConverter : PropertyFamilySegmentConverter
    {
        public override string MenuName => "Property Setter";
        protected override System.Type TargetType => typeof(PropertySetter);

        protected override Segment CreateFrom(in CommonData data)
        {
            return new PropertySetter
            {
                StartTime = data.StartTime,
                Property = data.Property,
                Value = data.Value,
            };
        }
    }

    /// Packs the selected segments into a single Sequence segment ("folder").
    [SegmentConverter]
    public sealed class PackIntoSequenceConverter : SegmentConverter
    {
        public override string MenuName => "Packed Sequence";

        public override bool CanConvert(IReadOnlyList<Segment> segments)
        {
            if (segments == null || segments.Count <= 1)
                return false;

            foreach (var segment in segments)
            {
                if (segment == null)
                    return false;
            }

            return true;
        }

        public override List<Segment> Convert(IReadOnlyList<Segment> segments)
        {
            float minStart = float.MaxValue;
            foreach (var segment in segments)
            {
                if (segment is IStartTimeConfigurable timed)
                    minStart = Mathf.Min(minStart, timed.GetStartTime());
            }

            if (minStart == float.MaxValue)
                minStart = 0f;

            var sequence = new Sequence
            {
                Name = "Packed Sequence",
                StartTime = minStart,
            };

            foreach (var segment in segments)
            {
                var clone = SegmentCloner.Clone(segment);
                if (clone == null)
                    continue;

                if (clone is IStartTimeConfigurable timed)
                    timed.SetStartTime(Mathf.Max(0f, timed.GetStartTime() - minStart));

                sequence.Segments.Add(clone);
            }

            return new List<Segment> { sequence };
        }
    }

    /// Unpacks a Sequence / InsertSequenceProvider / FindAndInsertSequenceProviders into
    /// individual segments, baking each child at its resolved timeline position.
    [SegmentConverter]
    public sealed class UnpackSequenceConverter : SegmentConverter
    {
        public override string MenuName => "Individual Segments";

        public override bool CanConvert(IReadOnlyList<Segment> segments)
        {
            if (segments == null || !segments.All(s => s is Sequence or InsertSequenceProvider or FindAndInsertSequenceProviders))
                return false;

            return true;
        }

        public override List<Segment> Convert(IReadOnlyList<Segment> segments)
        {
            var result = new List<Segment>();

            foreach(var source in segments)
            {
                var plan = source.GetPlan(null);
                if (plan?.Children == null || plan.Children.Count == 0)
                    return null;

                foreach (var childPlan in plan.Children)
                {
                    if (childPlan?.Segment == null)
                        continue;

                    var clone = SegmentCloner.Clone(childPlan.Segment);
                    if (clone == null)
                        continue;

                    if (clone is IStartTimeConfigurable timed)
                        timed.SetStartTime(Mathf.Max(0f, childPlan.Timing.AbsoluteStartTime));

                    result.Add(clone);
                }
            }
            
            return result;
        }
    }
}
