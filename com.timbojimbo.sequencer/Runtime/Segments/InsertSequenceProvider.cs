using System;
using JetBrains.Annotations;
using UnityEngine;


namespace TimboJimbo.Sequencer.Segments
{
    [Serializable]
    public class InsertSequenceProvider : Segment, IStartTimeConfigurable
    {
        public float StartTime;
        public SequenceProvider Provider;

        public void SetStartTime(float startTime) => StartTime = startTime;
        public float GetStartTime() => StartTime;

        private bool _isBuildingPlan = false;

        public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
        {
            if(_isBuildingPlan)
            {
                Debug.LogWarning("Detected recursive inclusion in InsertSequenceProvider. This is not supported and will likely lead to unexpected behaviour.");
                return new SegmentPlan(this, parent)
                {
                    Timing = { RelativeStartTime = StartTime, RelativeDuration = 0f }
                };
            }

            try
            {
                _isBuildingPlan = true;

                if(Provider != null)
                {
                    var plan = Provider.GetPlan(parent);
                    
                    // we are effectively hijacking it!
                    plan.Segment = this;
                    plan.Timing.RelativeStartTime = StartTime;

                    return plan;
                }
                else
                {
                    return new SegmentPlan(this, parent)
                    {
                        Timing = { RelativeStartTime = StartTime, RelativeDuration = 0f }
                    };
                }

            }
            finally
            {
                _isBuildingPlan = false;
            }

        }
    }
}