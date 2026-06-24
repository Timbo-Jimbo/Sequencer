using System;
using TimboJimbo.Sequencer;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    public sealed class SegmentSelectionModel : ScriptableObject
    {
        [NonSerialized]
        public SequenceProvider SourceProvider;

        [NonSerialized]
        public string SourcePropertyPath;

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

        public void InitializeFrom(SequenceProvider sourceProvider, Segment segment, string sourcePropertyPath)
        {
            SourceProvider = sourceProvider;
            SourcePropertyPath = sourcePropertyPath;
            // Use deep JSON copy of segment into ScriptableObject so mutations are local to proxy
            Segment = JsonUtility.FromJson(JsonUtility.ToJson(segment), segment.GetType()) as Segment;
            RefreshDisplayName();
        }

        public void RefreshFrom(SequenceProvider sourceProvider, Segment segment, string sourcePropertyPath)
        {
            SourceProvider = sourceProvider;
            SourcePropertyPath = sourcePropertyPath;
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
            if (string.IsNullOrEmpty(SourcePropertyPath))
            {
                name = nicifiedType;
                return;
            }

            name = $"{nicifiedType} ({SourcePropertyPath})";
        }

        public void CommitToProvider()
        {
            if (SourceProvider == null || string.IsNullOrEmpty(SourcePropertyPath))
                return;

            // Mutate provider inside the current Undo group
            Undo.RecordObject(SourceProvider, "Edit Segment");

            using var providerSo = new SerializedObject(SourceProvider);
            providerSo.Update();

            var targetProp = providerSo.FindProperty(SourcePropertyPath);
            if (targetProp != null)
            {
                targetProp.boxedValue = Segment;
                if (providerSo.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(SourceProvider);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(SourceProvider);
                    SegmentTimelineWindow.NotifyProviderChanged(SourceProvider);
                }
            }
        }
    }
}
