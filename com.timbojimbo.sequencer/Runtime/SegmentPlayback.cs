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

    /// <summary>
    /// Implemented by playbacks that can provide a "pre-roll" value for a property they
    /// drive - the value the property should hold from sequence start until the segment
    /// begins (e.g. a canvas-alpha tween 0 -> 1 queued 5s in should sit at 0, not pop).
    ///
    /// Participation is derived from the segment's plan: a segment declares the properties
    /// it drives via its <see cref="SegmentBindingsPlan"/> (just as it does for property
    /// binding). At compile time the earliest playback per unique property is determined,
    /// and - if it implements this interface - it is asked for a pre-extrapolation value
    /// during every setup pass. Custom segments get this behaviour by declaring their
    /// bindings in their plan and implementing this interface on their playback.
    /// </summary>
    public interface IPreExtrapolationSource
    {
        /// <summary>
        /// Called only when this playback is the earliest one driving <paramref name="property"/>
        /// in the whole sequence. Return true to have <paramref name="value"/> written up-front
        /// so the property holds it until this playback's segment starts.
        /// </summary>
        bool TryGetPreExtrapolationValue(BindableProperty property, out ValueContainer value);
    }

    public class NoOpPlayback : SegmentPlayback
    {
        public NoOpPlayback(in PlaybackBuildContext context) : base(context) { }
    }
}