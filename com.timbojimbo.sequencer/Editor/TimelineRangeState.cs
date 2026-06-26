using System;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    public sealed class TimelineRangeState : ScriptableObject
    {
        public const float MinDuration = 0.01f;

        [NonSerialized] private float _playhead = 0f;
        [SerializeField] private float _rangeStart = 0f;
        [SerializeField] private float _rangeEnd = 1f;
        [SerializeField] private bool _endAnchored = true;
        [SerializeField] private float _loopDelaySeconds = 0f;

        public float Playhead => _playhead;
        public float RangeStart => _rangeStart;
        public float RangeEnd => _rangeEnd;
        public bool EndAnchored => _endAnchored;
        public float LoopDelaySeconds => _loopDelaySeconds;

        public void Initialize(float duration)
        {
            _playhead = 0f;
            _rangeStart = 0f;
            _rangeEnd = Mathf.Max(duration, MinDuration);
            _endAnchored = true;
            _loopDelaySeconds = 0f;
        }

        public void SetPlayhead(float time, bool isPlaying)
        {
            _playhead = Mathf.Max(0f, time);
            if (isPlaying)
            {
                _playhead = Mathf.Clamp(_playhead, _rangeStart, _rangeEnd);
            }
            else
            {
                _playhead = Mathf.Clamp(_playhead, 0f, float.MaxValue);
            }
        }

        public void SetRange(float start, float end, float totalDuration)
        {
            _rangeStart = Mathf.Max(0f, start);
            float resolvedEnd = Mathf.Clamp(end, _rangeStart + MinDuration, totalDuration);
            _endAnchored = Mathf.Abs(resolvedEnd - totalDuration) <= 0.001f;
            _rangeEnd = resolvedEnd;
        }

        public void UpdateDuration(float totalDuration)
        {
            float limit = Mathf.Max(totalDuration, MinDuration);
            if (_endAnchored)
            {
                _rangeEnd = limit;
                _rangeStart = Mathf.Min(_rangeStart, _rangeEnd - MinDuration);
            }
            else
            {
                _rangeStart = Mathf.Clamp(_rangeStart, 0f, limit);
                _rangeEnd = Mathf.Clamp(_rangeEnd, _rangeStart + MinDuration, limit);
            }
        }

        public void ResetToDefault(float totalDuration)
        {
            _rangeStart = 0f;
            _rangeEnd = Mathf.Max(totalDuration, MinDuration);
            _endAnchored = true;
        }

        public void SetLoopDelay(float seconds)
        {
            _loopDelaySeconds = Mathf.Max(0f, seconds);
        }

        public TimboJimbo.Sequencer.PlaybackRange GetPlaybackRange()
        {
            return new TimboJimbo.Sequencer.PlaybackRange(_rangeStart, _rangeEnd, _endAnchored);
        }
    }
}
