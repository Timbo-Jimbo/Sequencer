using System;
using TimboJimbo.Sequencer;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    public sealed class SegmentSelectionModel : ScriptableObject
    {
        [SerializeField]
        private SegmentHandle _handle;

        [SerializeField]
        private float _resolvedStartTime;
        
        [SerializeField]
        private float _resolvedDuration;

        public SegmentHandle Handle
        {
            get => _handle;
            set => _handle = value;
        }

        [SerializeReference]
        public Segment Segment = new TimboJimbo.Sequencer.Segments.Sequence();

        public float StartTime
        {
            get => _resolvedStartTime;
            set
            {
                if (Segment is IStartTimeConfigurable st)
                {
                    st.SetStartTime(Mathf.Max(0f, value));
                    _resolvedStartTime = st.GetStartTime();
                }
            }
        }

        public float Duration
        {
            get => _resolvedDuration;
            set
            {
                if (Segment is IDurationConfigurable dur)
                {
                    dur.SetDuration(Mathf.Max(0.01f, value));
                    _resolvedDuration = dur.GetDuration();
                }
            }
        }

        public float EndTime => StartTime + Duration;

        public bool CanAdjustStartTime => Segment is IStartTimeConfigurable;
        public bool CanAdjustDuration => Segment is IDurationConfigurable;

        public void Bind(SequenceProvider sourceProvider, Segment segment, int index)
        {
            Handle = new SegmentHandle(sourceProvider, index);
            Segment = JsonUtility.FromJson(JsonUtility.ToJson(segment), segment.GetType()) as Segment;
            ResolveTimingFromPlan();
            RefreshDisplayName();
        }

        private void ResolveTimingFromPlan()
        {
            if (Segment == null)
                return;

            var plan = Segment.GetPlan(null);
            if (plan == null)
                return;
                
            _resolvedStartTime = plan.Timing.AbsoluteStartTime;
            _resolvedDuration = plan.Timing.AbsoluteDuration;
        }

        public void RefreshDisplayName()
        {
            if (Segment == null)
            {
                name = "Segment";
                return;
            }

            var nicifiedType = ObjectNames.NicifyVariableName(Segment.GetType().Name);
            name = $"{nicifiedType} (Index {Handle.Index})";
        }
    }
}
