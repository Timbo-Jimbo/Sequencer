using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace TimboJimbo.Sequencer.Segments
{
    [Serializable]
    [AddSegmentMenu("")]
    public class Sequence : Segment, IStartTimeConfigurable
    {
        public string Name;
        public float StartTime;

        [SerializeReference]
        public List<Segment> Segments = new List<Segment>();

        public void SetStartTime(float startTime) => StartTime = startTime;
        public float GetStartTime() => StartTime;

        /// <summary>Adds a segment and returns it for fluent programmatic authoring.</summary>
        public T Add<T>(T segment) where T : Segment
        {
            if (segment == null) throw new ArgumentNullException(nameof(segment));
            Segments.Add(segment);
            return segment;
        }

        public bool Remove(Segment segment) => segment != null && Segments.Remove(segment);

        public void ReplaceSegments(IEnumerable<Segment> segments)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            var replacement = new List<Segment>();
            foreach (var segment in segments)
            {
                if (segment == null) throw new ArgumentException("Sequence segments cannot contain null entries.", nameof(segments));
                replacement.Add(segment);
            }

            Segments.Clear();
            Segments.AddRange(replacement);
        }

        public void Clear() => Segments.Clear();

        public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
        {
            var plan = new SegmentPlan(this, parent)
            {
                Timing = { RelativeStartTime = StartTime }
            };

            var maxRelativeEndTime = 0f;

            foreach (var child in Segments)
            {
                var childPlan = child.GetPlan(plan);

                var childRelativeEndTime = childPlan.Timing.RelativeEndTime;
                if (childRelativeEndTime > maxRelativeEndTime)
                    maxRelativeEndTime = childRelativeEndTime;
            }

            plan.Timing.RelativeDuration = maxRelativeEndTime;

            return plan;
        }
    }
}