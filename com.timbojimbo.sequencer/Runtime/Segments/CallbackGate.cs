using System;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class CallbackGate : Segment, IStartTimeConfigurable, IPlaybackBuilder
{
    public float TriggerAt;
    public UnityEvent OnTriggered = new UnityEvent();
    public UnityEvent<PlaybackBoundaryContext> OnTriggeredWithContext = new UnityEvent<PlaybackBoundaryContext>();

    public void SetStartTime(float startTime) => TriggerAt = startTime;
    public float GetStartTime() => TriggerAt;

    public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
    {
        return new SegmentPlan(this, parent)
        {
            Timing = { RelativeStartTime = TriggerAt, RelativeDuration = 0f }
        };
    }

    public SegmentPlayback BuildPlayback(in PlaybackBuildContext context)
    {
        return new Playback(context, OnTriggered, OnTriggeredWithContext);
    }

    private class Playback : SegmentPlayback
    {
        private readonly UnityEvent _onTriggered;
        private readonly UnityEvent<PlaybackBoundaryContext> _onTriggeredWithContext;

        public Playback(
            in PlaybackBuildContext context,
            UnityEvent onTriggered,
            UnityEvent<PlaybackBoundaryContext> onTriggeredWithContext
        ) : base(in context)
        {
            _onTriggered = onTriggered;
            _onTriggeredWithContext = onTriggeredWithContext;
        }

        public override void OnEnter(in PlaybackBoundaryContext context)
        {
            base.OnEnter(context);
            _onTriggered?.Invoke();
            _onTriggeredWithContext?.Invoke(context);
        }
    }
}

public static class CallbackGateExtensions
{
    public static CallbackGate Callback(this SegMake _, UnityAction callback)
    {
        var result = new CallbackGate();
        result.OnTriggered.AddListener(callback);
        return result;
    }

    public static CallbackGate Callback(this SegMake _, UnityAction<PlaybackBoundaryContext> callback)
    {
        var result = new CallbackGate();
        result.OnTriggeredWithContext.AddListener(callback);
        return result;
    }

    public static CallbackGate Log(this SegMake _, string message)
    {
        return _.Callback(() => Debug.Log(message));
    }
}