using System;
using System.Collections.Generic;

public static class SequenceBuilderExtensions
{
    public static Segment If(
        this SegSchedule _,
        bool isTrue, 
        Segment then, 
        Segment otherwise = null
    ) => new ConditionalSegment
    {
        Condition = () => isTrue,
        Then = then,
        Otherwise = otherwise
    };

    public static Segment If(
        this SegSchedule _,
        Func<bool> isTrue, 
        Segment then, 
        Segment otherwise = null
    ) => new ConditionalSegment
    {
        Condition = isTrue,
        Then = then,
        Otherwise = otherwise
    };
    
    public static Segment Wait(
        this SegSchedule _,
        float seconds, 
        Segment then
    ) => CustomArrangement(
        _,
        ctx => seconds,
        then
    );

    public static Segment CustomArrangement(
        this SegSchedule _,
        Func<ArrangementContext, float> calculateAbsoluteStart,
        Segment s1,
        Segment s2,
        Segment s3 = null,
        Segment s4 = null,
        Segment s5 = null
    )
    {
        var result = new DynamicArrangementSegment
        {
            CalculateAbsoluteStart = calculateAbsoluteStart,
        };

        if (s1 != null) result.Segments.Add(s1);
        if (s2 != null) result.Segments.Add(s2);
        if (s3 != null) result.Segments.Add(s3);
        if (s4 != null) result.Segments.Add(s4);
        if (s5 != null) result.Segments.Add(s5);

        return result;
    }

    public static Segment CustomArrangement(
        this SegSchedule _,
        Func<ArrangementContext, float> calculateAbsoluteStart,
        IEnumerable<Segment> segments
    )
    {
        var result = new DynamicArrangementSegment
        {
            CalculateAbsoluteStart = calculateAbsoluteStart,
        };

        foreach (var segment in segments)
        {
            if (segment != null) result.Segments.Add(segment);
        }

        return result;
    }

    public static Segment CustomArrangement(
        this SegSchedule _,
        Func<ArrangementContext, float> calculateAbsoluteStart,
        params Segment[] segments
    ) => CustomArrangement(_, calculateAbsoluteStart, (IEnumerable<Segment>)segments);

    public static Segment OneAfterAnother(
        this SegSchedule _,
        Segment s1, 
        Segment s2, 
        Segment s3 = null, 
        Segment s4 = null, 
        Segment s5 = null
    ) => CustomArrangement(
        _,
        ctx => ctx.HasPrevious ? ctx.Previous.AbsoluteEnd : 0f,
        s1,
        s2,
        s3,
        s4,
        s5
    );

    public static Segment OneAfterAnother(
        this SegSchedule _,
        IEnumerable<Segment> segments
    ) => CustomArrangement(
        _,
        ctx => ctx.HasPrevious ? ctx.Previous.AbsoluteEnd : 0f,
        segments
    );

    public static Segment OneAfterAnother(
        this SegSchedule _,
        params Segment[] segments
    ) => OneAfterAnother(_, (IEnumerable<Segment>)segments);

    public static Segment Together(
        this SegSchedule _,
        Segment s1, 
        Segment s2, 
        Segment s3 = null, 
        Segment s4 = null, 
        Segment s5 = null
    ) => CustomArrangement(
        _,
        ctx => 0f,
        s1,
        s2,
        s3,
        s4,
        s5
    );

    public static Segment Together(
        this SegSchedule _,
        IEnumerable<Segment> segments
    ) => CustomArrangement(
        _,
        ctx => 0f,
        segments
    );

    public static Segment Together(
        this SegSchedule _,
        params Segment[] segments
    ) => Together(_, (IEnumerable<Segment>)segments);

    public static Segment Stagger(
        this SegSchedule _,
        float seconds,
        Segment s1,
        Segment s2,
        Segment s3 = null,
        Segment s4 = null,
        Segment s5 = null
    ) => CustomArrangement(
        _,
        ctx => ctx.Index * seconds,
        s1,
        s2,
        s3,
        s4,
        s5
    );

    public static Segment Stagger(
        this SegSchedule _,
        float seconds,
        IEnumerable<Segment> segments
    ) => CustomArrangement(
        _,
        ctx => ctx.Index * seconds,
        segments
    );

    public static Segment Stagger(
        this SegSchedule _,
        float seconds,
        params Segment[] segments
    ) => Stagger(_, seconds, (IEnumerable<Segment>)segments);

    public static Segment ProportionalStagger(
        this SegSchedule _,
        Segment s1,
        Segment s2,
        Segment s3 = null,
        Segment s4 = null,
        Segment s5 = null,
        float percent01 = 0.5f
    ) => CustomArrangement(
        _,
        ctx => ctx.HasPrevious
            ? ctx.Previous.AbsoluteStart + (ctx.Previous.Duration * percent01)
            : 0f,
        s1,
        s2,
        s3,
        s4,
        s5
    );

    public static Segment ProportionalStagger(
        this SegSchedule _,
        IEnumerable<Segment> segments,
        float percent01 = 0.5f
    ) => CustomArrangement(
        _,
        ctx => ctx.HasPrevious
            ? ctx.Previous.AbsoluteStart + (ctx.Previous.Duration * percent01)
            : 0f,
        segments
    );

    public static Segment ProportionalStagger(
        this SegSchedule _,
        float percent01,
        params Segment[] segments
    ) => ProportionalStagger(_, segments, percent01);

    
    public readonly struct ArrangementTiming
    {
        public float AbsoluteStart { get; }
        public float AbsoluteEnd { get; }
        public float Duration => AbsoluteEnd - AbsoluteStart;

        public ArrangementTiming(float absoluteStart, float absoluteEnd)
        {
            AbsoluteStart = absoluteStart;
            AbsoluteEnd = absoluteEnd;
        }
    }

    public readonly struct ArrangementContext
    {
        public int Index { get; }
        public int Count { get; }

        public bool HasPrevious { get; }
        public bool HasNext { get; }

        public ArrangementTiming Previous { get; }
        public ArrangementTiming Current { get; }
        public ArrangementTiming Next { get; }

        public ArrangementContext(
            int index,
            int count,
            bool hasPrevious,
            bool hasNext,
            ArrangementTiming previous,
            ArrangementTiming current,
            ArrangementTiming next
        )
        {
            Index = index;
            Count = count;
            HasPrevious = hasPrevious;
            HasNext = hasNext;
            Previous = previous;
            Current = current;
            Next = next;
        }
    }

    private class DynamicArrangementSegment : Segment
    {
        public Func<ArrangementContext, float> CalculateAbsoluteStart;
        public List<Segment> Segments = new List<Segment>();

        public override SegmentPlan GetPlan(SegmentPlan parent)
        {
            var blueprint = new SegmentPlan(this, parent);

            var childPlans = new List<SegmentPlan>(Segments.Count);
            for (int i = 0; i < Segments.Count; i++)
                childPlans.Add(Segments[i].GetPlan(blueprint));

            var count = childPlans.Count;
            if (count == 0)
            {
                blueprint.Timing.RelativeDuration = 0f;
                return blueprint;
            }

            var intrinsicStarts = new float[count];
            var intrinsicEnds = new float[count];

            for (int i = 0; i < count; i++)
            {
                intrinsicStarts[i] = childPlans[i].Timing.RelativeStartTime;
                intrinsicEnds[i] = childPlans[i].Timing.RelativeEndTime;
            }

            var resolvedStarts = new float[count];
            var resolvedEnds = new float[count];
            var maxRelativeEnd = 0f;

            for (int i = 0; i < count; i++)
            {
                var hasPrevious = i > 0;
                var hasNext = i < count - 1;

                var previous = hasPrevious
                    ? new ArrangementTiming(resolvedStarts[i - 1], resolvedEnds[i - 1])
                    : default;

                var current = new ArrangementTiming(intrinsicStarts[i], intrinsicEnds[i]);

                var next = hasNext
                    ? new ArrangementTiming(intrinsicStarts[i + 1], intrinsicEnds[i + 1])
                    : default;

                var context = new ArrangementContext(
                    index: i,
                    count: count,
                    hasPrevious: hasPrevious,
                    hasNext: hasNext,
                    previous: previous,
                    current: current,
                    next: next);

                var start = CalculateAbsoluteStart != null
                    ? CalculateAbsoluteStart(context)
                    : current.AbsoluteStart;

                if (float.IsNaN(start) || float.IsInfinity(start))
                    start = current.AbsoluteStart;

                childPlans[i].Timing.RelativeStartTime = start;
                resolvedStarts[i] = start;

                var end = childPlans[i].Timing.RelativeEndTime;
                resolvedEnds[i] = end;
                if (end > maxRelativeEnd)
                    maxRelativeEnd = end;
            }

            blueprint.Timing.RelativeDuration = maxRelativeEnd;
            return blueprint;
        }
    }

    private class OffsetSegment : Segment
    {
        public float Offset;
        public Segment InnerSegment;

        public override SegmentPlan GetPlan(SegmentPlan parent)
        {
            var blueprint = new SegmentPlan(this, parent);

            var innerPlan = InnerSegment.GetPlan(blueprint);
            innerPlan.Timing.RelativeStartTime += Offset;

            return blueprint;
        }
    }

    private class ConditionalSegment : Segment
    {
        public Func<bool> Condition;
        public Segment Then;
        public Segment Otherwise;

        public override SegmentPlan GetPlan(SegmentPlan parent)
        {
            var blueprint = new SegmentPlan(this, parent);

            var chosenSegment = Condition() ? Then : Otherwise;

            if (chosenSegment != null)
            {
                var chosenPlan = chosenSegment.GetPlan(blueprint);
                blueprint.Timing.RelativeDuration = chosenPlan.Timing.RelativeEndTime;
            }

            return blueprint;
        }
    }
}