using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using TimboJimbo.PropertyBindings;
using UnityEngine;

public class SegmentPlan
{
    public Segment Segment;
    public SegmentPlan Parent;
    public List<SegmentPlan> Children;
    public SegmentTimingPlan Timing;
    public SegmentBindingsPlan Bindings;
    public bool CanAdjustStartTime => Segment is IStartTimeConfigurable;
    public bool CanAdjustDuration => Segment is IDurationConfigurable;

    public SegmentPlan(
        Segment segment,
        SegmentPlan parent = null
    )
    {
        Segment = segment;
        Timing = new SegmentTimingPlan(this);
        Bindings = new SegmentBindingsPlan(this);

        Parent = parent;
        Children = new List<SegmentPlan>();
        Parent?.Children.Add(this);
    }
}

public class SegmentBindingsPlan
{
    public SegmentPlan Plan;
    public GameObject BindingsRoot
    {
        get => _bindingRoot != null ? _bindingRoot : Plan.Parent?.Bindings.BindingsRoot;
        set => _bindingRoot = value;
    }
    public HashSet<BindableProperty> Properties;

    private GameObject _bindingRoot;

    public SegmentBindingsPlan(SegmentPlan plan)
    {
        Plan = plan;
        Properties = new HashSet<BindableProperty>();
    }
}

public class SegmentTimingPlan
{
    public SegmentPlan Plan;
    public float RelativeStartTime;
    public float RelativeDuration;
    public float RelativeEndTime => RelativeStartTime + RelativeDuration;

    public float AbsoluteStartTime => Plan.Parent != null
        ? Plan.Parent.Timing.AbsoluteStartTime + RelativeStartTime
        : RelativeStartTime;

    public float AbsoluteDuration => RelativeDuration;
    public float AbsoluteEndTime => AbsoluteStartTime + AbsoluteDuration;

    public SegmentTimingPlan(SegmentPlan plan)
    {
        Plan = plan;
    }
}

public interface IDurationConfigurable
{
    void SetDuration(float duration);
    float GetDuration();
}

public interface IStartTimeConfigurable
{
    void SetStartTime(float startTime);
    float GetStartTime();
}

public interface IPlaybackBuilder
{
    SegmentPlayback BuildPlayback(in PlaybackBuildContext context);
}

[Serializable]
public abstract class Segment
{
    public abstract SegmentPlan GetPlan([CanBeNull] SegmentPlan parent);
}
