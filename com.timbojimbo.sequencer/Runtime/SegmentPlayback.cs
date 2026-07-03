using System.Collections.Generic;
using TimboJimbo.PropertyBindings;
using UnityEngine;

namespace TimboJimbo.Sequencer
{
    public readonly struct PlaybackBuildContext
    {
        public readonly PropertyBindingCollection PropertyBindings;
        public readonly float AbsoluteStartTime;
        public readonly float AbsoluteDuration;

        public PlaybackBuildContext(PropertyBindingCollection propertyBindings, float absoluteStartTime, float absoluteDuration)
        {
            PropertyBindings = propertyBindings;
            AbsoluteStartTime = absoluteStartTime;
            AbsoluteDuration = absoluteDuration;
        }
    }

    public enum SegmentEvaluationMode
    {
        Playback,
        Scrub
    }

    public readonly struct PlaybackSetupContext
    {
        public readonly SequencePlayer Sequence;
        public readonly IReadOnlyList<SegmentPlayback> Playbacks;
        public readonly bool IsPreview;

        public PlaybackSetupContext(SequencePlayer sequence, IReadOnlyList<SegmentPlayback> playbacks, bool isPreview)
        {
            Sequence = sequence;
            Playbacks = playbacks;
            IsPreview = isPreview;
        }
    }

    public readonly struct PlaybackBoundaryContext
    {
        public readonly SequencePlayer Sequence;
        public readonly float Playhead;
        public readonly SegmentEvaluationMode EvaluationMode;
        public readonly bool IsPreview;

        public bool IsScrubbing => EvaluationMode == SegmentEvaluationMode.Scrub;
        public bool IsJump => EvaluationMode == SegmentEvaluationMode.Scrub;

        public PlaybackBoundaryContext(
            SequencePlayer sequence,
            float playhead,
            SegmentEvaluationMode evaluationMode,
            bool isPreview)
        {
            Sequence = sequence;
            Playhead = playhead;
            EvaluationMode = evaluationMode;
            IsPreview = isPreview;
        }
    }

    public readonly struct PlaybackSampleContext
    {
        public readonly SequencePlayer Sequence;
        public readonly float Playhead;
        public readonly float LocalTime;
        public readonly float Duration;
        public readonly SegmentEvaluationMode EvaluationMode;
        public readonly bool IsPreview;

        public float NormalizedTime => Duration > 0f
            ? Mathf.Clamp01(LocalTime / Duration)
            : (LocalTime >= 0f ? 1f : 0f);

        public bool IsScrubbing => EvaluationMode == SegmentEvaluationMode.Scrub;
        public float AbsoluteTime => Playhead;

        public PlaybackSampleContext(
            SequencePlayer sequence,
            float playhead,
            float localTime,
            float duration,
            SegmentEvaluationMode evaluationMode,
            bool isPreview)
        {
            Sequence = sequence;
            Playhead = playhead;
            LocalTime = localTime;
            Duration = duration;
            EvaluationMode = evaluationMode;
            IsPreview = isPreview;
        }
    }

    public abstract class SegmentPlayback
    {
        public int ExecutionOrder;
        public float AbsoluteStartTime;
        public float AbsoluteDuration;
        public float AbsoluteEndTime => AbsoluteStartTime + AbsoluteDuration;

        protected SegmentPlayback(in PlaybackBuildContext context)
        {
            AbsoluteStartTime = context.AbsoluteStartTime;
            AbsoluteDuration = context.AbsoluteDuration;
        }

        public virtual void Setup(in PlaybackSetupContext context) { }
        public virtual void OnEnter(in PlaybackBoundaryContext context) { }
        public virtual void OnSample(in PlaybackSampleContext context) { }
        public virtual void OnExit(in PlaybackBoundaryContext context) { }
        public virtual void CleanUp(in PlaybackSetupContext context) { }
    }

    public class NoOpPlayback : SegmentPlayback
    {
        public NoOpPlayback(in PlaybackBuildContext context) : base(context) { }
    }
}