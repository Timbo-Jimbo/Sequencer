using System;
using JetBrains.Annotations;
using TimboJimbo.Core;
using TimboJimbo.Sequencer.Builder;
using UnityEngine.Events;


namespace TimboJimbo.Sequencer.Segments
{
    [Serializable]
    public class CustomTweener : Segment, IStartTimeConfigurable, IDurationConfigurable, IPlaybackBuilder
    {
        public float StartTime;
        public float Duration;
        public EaseType Ease;
        public UnityEvent<float> OnSample;
        public UnityEvent<float, CustomTweenerSampleContext> OnSampleWithContext;

        public void SetDuration(float duration) => Duration = duration;
        public float GetDuration() => Duration;
        public void SetStartTime(float startTime) => StartTime = startTime;
        public float GetStartTime() => StartTime;

        public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
        {
            var blueprint = new SegmentPlan(this, parent)
            {
                Timing = { RelativeStartTime = StartTime, RelativeDuration = Duration }
            };

            return blueprint;
        }

        public SegmentPlayback BuildPlayback(in PlaybackBuildContext context)
        {
            return new Playback(context)
            {
                Ease = Ease,
                Duration = context.AbsoluteDuration,
                OnSampleCallback = OnSample.Invoke,
                OnSampleWithContextCallback = OnSampleWithContext.Invoke
            };
        }

        private class Playback : SegmentPlayback
        {
            public EaseType Ease;
            public float Duration;
            public Action<float> OnSampleCallback;
            public Action<float, CustomTweenerSampleContext> OnSampleWithContextCallback;

            private bool _hasEntered;

            public Playback(in PlaybackBuildContext context) : base(in context)
            {
            }
            public override void OnEnter(in PlaybackBoundaryContext context)
            {
                base.OnEnter(context);
                OnSampleCallback?.Invoke(0f);
                OnSampleWithContextCallback?.Invoke(0f, new CustomTweenerSampleContext { NormalizedTime = 0f, Duration = Duration });
                _hasEntered = true;
            }

            public override void OnSample(in PlaybackSampleContext context)
            {
                var easedT = EaseUtility.Evaluate(context.NormalizedTime, Ease);
                OnSampleCallback?.Invoke(easedT);
                OnSampleWithContextCallback?.Invoke(easedT, new CustomTweenerSampleContext { NormalizedTime = context.NormalizedTime, Duration = Duration });
            }

            public override void OnExit(in PlaybackBoundaryContext context)
            {
                base.OnExit(context);
                OnSampleCallback?.Invoke(1f);
                OnSampleWithContextCallback?.Invoke(1f, new CustomTweenerSampleContext { NormalizedTime = 1f, Duration = Duration });
                _hasEntered = false;
            }

            public override void CleanUp(in PlaybackSetupContext context)
            {
                base.CleanUp(context);
                if (_hasEntered)
                {
                    OnSampleCallback?.Invoke(1f);
                    OnSampleWithContextCallback?.Invoke(1f, new CustomTweenerSampleContext { NormalizedTime = 1f, Duration = Duration });
                }

                _hasEntered = false;
                OnSampleCallback = null;
                OnSampleWithContextCallback = null;
            }
        }
    }

    public struct CustomTweenerSampleContext
    {
        public float NormalizedTime;
        public float Duration;
    }

    public static class CustomerTweenerExtensions
    {
        public static CustomTweener TweenCustom(
            this SegMake _, 
            UnityAction<float> onSample = null, 
            float duration = 1f, 
            EaseType ease = EaseType.Linear
        )
        {
            var customTweener = new CustomTweener
            {
                Duration = duration,
                Ease = ease,
                OnSample = new UnityEvent<float>()
            };

            if (onSample != null)
                customTweener.OnSample.AddListener(onSample);

            return customTweener;
        }

        public static CustomTweener TweenCustom(
            this SegMake _, 
            UnityAction<float, CustomTweenerSampleContext> onSampleWithContext = null, 
            float duration = 1f, 
            EaseType ease = EaseType.Linear
        )
        {
            var customTweener = new CustomTweener
            {
                Duration = duration,
                Ease = ease,
                OnSampleWithContext = new UnityEvent<float, CustomTweenerSampleContext>()
            };

            if (onSampleWithContext != null)
                customTweener.OnSampleWithContext.AddListener(onSampleWithContext);

            return customTweener;
        }
    }
}