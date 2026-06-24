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
        public GameObject BindingRoot;

        [SerializeReference]
        public List<Segment> Segments = new List<Segment>();

        public void SetStartTime(float startTime) => StartTime = startTime;
        public float GetStartTime() => StartTime;

        public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
        {
            var plan = new SegmentPlan(this, parent)
            {
                Bindings = { BindingsRoot = BindingRoot },
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