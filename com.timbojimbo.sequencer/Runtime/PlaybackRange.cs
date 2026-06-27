using System;
using UnityEngine;

namespace TimboJimbo.Sequencer
{
    [Serializable]
    public struct PlaybackRange
    {
        public float Start;
        public float End;
        public bool EndAnchoredToTimelineEnd;

        public PlaybackRange(float start, float end, bool endAnchoredToTimelineEnd = false)
        {
            Start = Mathf.Max(0f, start);
            End = Mathf.Max(Start, end);
            EndAnchoredToTimelineEnd = endAnchoredToTimelineEnd;
        }

        public static PlaybackRange FullTimeline()
        {
            return new PlaybackRange(0f, 1f, endAnchoredToTimelineEnd: true);
        }

        public float GetResolvedStart() => Mathf.Max(0f, Start);

        public float GetResolvedEnd(float timelineDuration)
        {
            float start = GetResolvedStart();
            float end = EndAnchoredToTimelineEnd ? timelineDuration : End;
            return Mathf.Max(start, end);
        }

        public PlaybackRange Normalize(float timelineDuration)
        {
            float start = Mathf.Max(0f, Start);
            float end = EndAnchoredToTimelineEnd ? timelineDuration : End;
            end = Mathf.Max(start, end);

            if (!EndAnchoredToTimelineEnd)
                end = Mathf.Min(end, Mathf.Max(start, timelineDuration));

            return new PlaybackRange(start, end, EndAnchoredToTimelineEnd);
        }

        public void NormalizeToDuration(float timelineDuration)
        {
            this = Normalize(timelineDuration);
        }

        public void CopyFrom(PlaybackRange other)
        {
            Start = other.Start;
            End = other.End;
            EndAnchoredToTimelineEnd = other.EndAnchoredToTimelineEnd;
        }

        public void SetRange(float start, float end, bool endAnchoredToTimelineEnd)
        {
            Start = start;
            End = end;
            EndAnchoredToTimelineEnd = endAnchoredToTimelineEnd;
        }

        public void SetDefault()
        {
            Start = 0f;
            End = 1f;
            EndAnchoredToTimelineEnd = true;
        }

        public PlaybackRange WithStart(float start) => new PlaybackRange(start, End, EndAnchoredToTimelineEnd);

        public PlaybackRange WithEnd(float end) => new PlaybackRange(Start, end, EndAnchoredToTimelineEnd);

        public PlaybackRange WithEndAnchoredToTimelineEnd(bool anchored) => new PlaybackRange(Start, End, anchored);
    }
}
