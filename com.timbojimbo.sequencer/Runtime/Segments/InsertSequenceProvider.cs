using System;
using JetBrains.Annotations;

[Serializable]
public class InsertSequenceProvider : Segment, IStartTimeConfigurable
{
    public float StartTime;
    public SequenceProvider Provider;

    public void SetStartTime(float startTime) => StartTime = startTime;
    public float GetStartTime() => StartTime;

    public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
    {
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
}
