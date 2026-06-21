using System;
using System.Threading;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class PauseGate : Segment, IStartTimeConfigurable, IPlaybackBuilder
{
    public float PauseAt;

    [SerializeReference]
    public PauseGateResumeCheckerFactory ResumeChecker;

    public void SetStartTime(float startTime) => PauseAt = startTime;
    public float GetStartTime() => PauseAt;

    public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
    {
        return new SegmentPlan(this, parent)
        {
            Timing = { RelativeStartTime = PauseAt, RelativeDuration = 0f }
        };
    }

    public SegmentPlayback BuildPlayback(in PlaybackBuildContext context)
    {
        return new Playback(context, ResumeChecker);
    }

    private class Playback : SegmentPlayback
    {
        private readonly PauseGateResumeCheckerFactory _checkerFactory;
        private IPauseGateResumeChecker _checker;
        private CancellationTokenSource _watchCts;

        public Playback(
            in PlaybackBuildContext context,
            PauseGateResumeCheckerFactory checkerFactory
        ) : base(in context)
        {
            _checkerFactory = checkerFactory;
        }

        public override void Setup(in PlaybackSetupContext context)
        {
            base.Setup(context);

            if (_checkerFactory == null)
                return;

            _checker = _checkerFactory.Create();
            _checker.Setup();
        }

        public override void OnEnter(in PlaybackBoundaryContext context)
        {
            base.OnEnter(context);

            if (context.IsPreview || context.EvaluationMode == SegmentEvaluationMode.Scrub)
                return;

            context.Sequence.Pause();
            _checker?.Reset();
            
            _watchCts = new CancellationTokenSource();
            var _ = WatchForResumeAsync(context.Sequence, _watchCts.Token);
        }

        public override void CleanUp(in PlaybackSetupContext context)
        {
            base.CleanUp(context);
            _watchCts?.Cancel();
            _watchCts?.Dispose();
            _checker?.CleanUp();
        }

        private async Awaitable WatchForResumeAsync(SequenceInstance sequence, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_checker != null && _checker.ShouldResume)
                {
                    sequence.Resume();
                    return;
                }

                await Awaitable.NextFrameAsync(ct);
            }
        }
    }
}

public abstract class PauseGateResumeCheckerFactory
{
    public abstract IPauseGateResumeChecker Create();
}

public interface IPauseGateResumeChecker
{
    bool ShouldResume { get; }
    void Setup();
    void CleanUp();
    void Reset();
}

internal sealed class ConditionalResumeCheckerFactory : PauseGateResumeCheckerFactory
{
    private readonly Func<bool> _condition;

    public ConditionalResumeCheckerFactory(Func<bool> condition)
    {
        _condition = condition;
    }

    public override IPauseGateResumeChecker Create()
    {
        return new Checker(_condition);
    }

    private class Checker : IPauseGateResumeChecker
    {
        private readonly Func<bool> _condition;
        private bool _triggered;

        public bool ShouldResume
        {
            get
            {
                if(_triggered)
                    return true;

                return _triggered = _condition();
            }
        }


        public Checker(Func<bool> condition)
        {
            _condition = condition;
        }

        public void Setup()
        {
        }

        public void CleanUp()
        {
        }

        public void Reset()
        {
            _triggered = false;
        }
    }

}

[Serializable]
internal sealed class ButtonClickResumeCheckerFactory : PauseGateResumeCheckerFactory
{
    public Button Button;

    public ButtonClickResumeCheckerFactory(Button button)
    {
        Button = button;
    }

    public override IPauseGateResumeChecker Create()
    {
        return new Checker(Button);
    }

    private class Checker : IPauseGateResumeChecker
    {
        private readonly Button _button;
        private bool _buttonClicked;

        public bool ShouldResume => _buttonClicked;

        public void Reset() => _buttonClicked = false;

        public Checker(Button button)
        {
            _button = button;
        }

        public void Setup()
        {
            _button.onClick.AddListener(OnClick);
        }

        public void CleanUp()
        {
            _button.onClick.RemoveListener(OnClick);
            _buttonClicked = false;
        }

        private void OnClick()
        {
            _buttonClicked = true;
        }
    }
}

[Serializable]
internal sealed class WaitSecondsResumeCheckerFactory : PauseGateResumeCheckerFactory
{
    public float Seconds;
    
    public WaitSecondsResumeCheckerFactory(float seconds)
    {
        Seconds = seconds;
    }

    public override IPauseGateResumeChecker Create()
    {
        return new Checker(Seconds);
    }

    private class Checker : IPauseGateResumeChecker
    {
        private readonly float _seconds;
        private float _resumeAt;

        public bool ShouldResume => Time.time >= _resumeAt;

        public void Reset() => _resumeAt = Time.time + _seconds;

        public Checker(float seconds)
        {
            _seconds = seconds;
        }

        public void Setup()
        {
        }

        public void CleanUp()
        {
        }
    }
}

public static class PauseGateExtensions
{
    public static Segment Pause(
        this SegSchedule _, 
        float seconds
    )
    {
        return new PauseGate
        {
            ResumeChecker = new WaitSecondsResumeCheckerFactory(seconds),
        };
    }

    public static Segment PauseUntil(
        this SegSchedule _, 
        Func<bool> returnsTrue
    )
    {
        return new PauseGate
        {
            ResumeChecker = new ConditionalResumeCheckerFactory(returnsTrue),
        };
    }

    public static Segment PauseWhile(
        this SegSchedule _, 
        Func<bool> returnsTrue
    )
    {
        return new PauseGate
        {
            ResumeChecker = new ConditionalResumeCheckerFactory(() => !returnsTrue()),
        };
    }

    public static Segment PauseUntilClick(
        this SegSchedule _, 
        Button button
    )
    {
        return new PauseGate
        {
            ResumeChecker = new ButtonClickResumeCheckerFactory(button),
        };
    }
}