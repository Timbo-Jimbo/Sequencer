using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimboEditor.Sequencer.Segments
{
    [CustomSegmentRecorder(typeof(PropertyTweener))]
    public sealed class PropertyTweenerRecorder : SegmentRecorder
    {
        public override int Priority => -100;

        public override bool CanConsume(Segment segment, BindableProperty property, float time)
        {
            if (segment is not PropertyTweener tweener)
                return false;

            if (tweener.Property == null || !tweener.Property.Equals(property))
                return false;

            float start = tweener.StartTime;
            float end = tweener.StartTime + tweener.Duration;
            const float epsilon = 0.05f;
            return time >= start - epsilon && time <= end + epsilon;
        }

        public override void Consume(Segment segment, BindableProperty property, ValueContainer value, float time)
        {
            if (segment is PropertyTweener tweener)
                tweener.EndValue = value;
        }

        public override bool CanCreateFor(BindableProperty property)
        {
            return true;
        }

        public override Segment CreateSegment(BindableProperty property, ValueContainer value, float time)
        {
            float duration = Mathf.Clamp(time, 0.01f, 1.0f);
            return new PropertyTweener
            {
                StartTime = time - duration,
                Duration = duration,
                Property = property,
                StartMode = EasedStartMode.StartFromCurrent,
                EndMode = EasedEndMode.EndAtAbsolute,
                EndValue = value
            };
        }
    }
}