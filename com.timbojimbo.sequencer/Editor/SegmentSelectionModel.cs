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

        public SegmentHandle Handle
        {
            get => _handle;
            set => _handle = value;
        }

        [SerializeReference]
        public Segment Segment = new TimboJimbo.Sequencer.Segments.Sequence();

        public float StartTime
        {
            get => Segment is IStartTimeConfigurable st ? st.GetStartTime() : 0f;
            set
            {
                if (Segment is IStartTimeConfigurable st)
                {
                    Undo.RecordObject(this, "Adjust Timing");
                    st.SetStartTime(Mathf.Max(0f, value));
                }
            }
        }

        public float Duration
        {
            get => Segment is IDurationConfigurable dur ? dur.GetDuration() : 0f;
            set
            {
                if (Segment is IDurationConfigurable dur)
                {
                    Undo.RecordObject(this, "Adjust Duration");
                    dur.SetDuration(Mathf.Max(0.01f, value));
                }
            }
        }

        public float EndTime => StartTime + Duration;

        public bool CanAdjustStartTime => Segment is IStartTimeConfigurable;
        public bool CanAdjustDuration => Segment is IDurationConfigurable;

        public void InitializeFrom(SequenceProvider sourceProvider, Segment segment, int index)
        {
            Handle = new SegmentHandle(sourceProvider, index);
            Segment = JsonUtility.FromJson(JsonUtility.ToJson(segment), segment.GetType()) as Segment;
            RefreshDisplayName();
        }

        public void RefreshFrom(SequenceProvider sourceProvider, Segment segment, int index)
        {
            Handle = new SegmentHandle(sourceProvider, index);
            Segment = JsonUtility.FromJson(JsonUtility.ToJson(segment), segment.GetType()) as Segment;
            RefreshDisplayName();
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

        public void CommitToProvider()
        {
            if (Handle.Provider == null)
                return;

            Undo.RecordObject(Handle.Provider, "Edit Segment");

            using var providerSo = new SerializedObject(Handle.Provider);
            providerSo.Update();

            var targetProp = providerSo.FindProperty(Handle.PropertyPath);
            if (targetProp != null)
            {
                targetProp.boxedValue = Segment;
                if (providerSo.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(Handle.Provider);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(Handle.Provider);
                    SegmentTimelineWindow.NotifyProviderChanged(Handle.Provider);
                }
            }
        }
    }
}
