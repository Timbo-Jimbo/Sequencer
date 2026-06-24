using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;

namespace TimboJimboEditor.Sequencer
{
    public sealed class TimelineSessionState : IDisposable
    {
        public SequenceProvider Provider { get; private set; }
        public List<SegmentSelectionModel> Models { get; } = new();

        public event Action SessionRefreshed;

        public void Bind(SequenceProvider provider)
        {
            Provider = provider;
            Refresh();
        }

        public void Refresh()
        {
            if (Provider == null || Provider.Sequence == null)
            {
                ClearModels();
                SessionRefreshed?.Invoke();
                return;
            }

            var activeLayer = Provider.Sequence.Segments;
            
            // Build dictionary of unique resurrected/existing selection models by index
            var existingByIndex = new Dictionary<int, SegmentSelectionModel>();

            // 1) Read from existing session Models
            for (int i = 0; i < Models.Count; i++)
            {
                var m = Models[i];
                if (m != null && ReferenceEquals(m.Handle.Provider, Provider))
                {
                    existingByIndex[m.Handle.Index] = m;
                }
            }

            // 2) Read from active Unity selection (essential to capture undo-resurrected instances)
            var currentSelections = Selection.objects.OfType<SegmentSelectionModel>();
            foreach (var m in currentSelections)
            {
                if (m != null && m.Handle.Provider == Provider)
                {
                    existingByIndex[m.Handle.Index] = m;
                }
            }

            var nextModels = new List<SegmentSelectionModel>();

            for (int i = 0; i < activeLayer.Count; i++)
            {
                var segment = activeLayer[i];
                if (segment == null)
                    continue;

                if (existingByIndex.TryGetValue(i, out var reused) && reused != null)
                {
                    reused.RefreshFrom(Provider, segment, i);
                    nextModels.Add(reused);
                    existingByIndex.Remove(i);
                }
                else
                {
                    var model = ScriptableObject.CreateInstance<SegmentSelectionModel>();
                    model.hideFlags = HideFlags.DontSave;
                    model.InitializeFrom(Provider, segment, i);
                    nextModels.Add(model);
                }
            }

            // Cleanup stale models that don't match any index anymore
            foreach (var stale in existingByIndex.Values)
            {
                if (stale != null)
                {
                    UnityEngine.Object.DestroyImmediate(stale);
                }
            }

            Models.Clear();
            Models.AddRange(nextModels);

            SessionRefreshed?.Invoke();
        }

        public void CommitAll()
        {
            if (Provider == null)
                return;

            Undo.RecordObject(Provider, "Commit Session Changes");
            for (int i = 0; i < Models.Count; i++)
            {
                Models[i].CommitToProvider();
            }
            Refresh();
        }

        public void AddSegment(Type type, float time)
        {
            if (Provider == null || Provider.Sequence == null)
                return;

            Segment created;
            try
            {
                created = (Segment)Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not create segment {type.Name}: {e.Message}");
                return;
            }

            if (created is IStartTimeConfigurable timeConfig)
                timeConfig.SetStartTime(Mathf.Max(0f, time));

            Undo.RecordObject(Provider, $"Add {type.Name}");
            Provider.Sequence.Segments.Add(created);

            EditorUtility.SetDirty(Provider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(Provider);

            Refresh();
        }

        public void DeleteSegments(IReadOnlyList<SegmentSelectionModel> segmentModels)
        {
            if (Provider == null || Provider.Sequence == null || segmentModels == null || segmentModels.Count == 0)
                return;

            List<int> deletedIndices = new List<int>();
            HashSet<Segment> targetsToDelete = new HashSet<Segment>();

            foreach (var m in segmentModels)
            {
                if (m == null) continue;
                int index = m.Handle.Index;
                if (index >= 0 && index < Provider.Sequence.Segments.Count)
                {
                    deletedIndices.Add(index);
                    targetsToDelete.Add(Provider.Sequence.Segments[index]);
                }
            }

            if (targetsToDelete.Count == 0)
                return;

            // Sort deleted indices ascending so we can calculate shifts deterministically
            deletedIndices.Sort();

            // Record Undo and apply index shifting for each surviving model
            foreach (var model in Models)
            {
                if (model == null) continue;
                int oldIndex = model.Handle.Index;
                
                // Only shift surviving models
                if (!deletedIndices.Contains(oldIndex))
                {
                    int shift = 0;
                    foreach (int deletedIdx in deletedIndices)
                    {
                        if (deletedIdx < oldIndex)
                        {
                            shift++;
                        }
                    }

                    if (shift > 0)
                    {
                        Undo.RecordObject(model, "Delete Segments");
                        model.Handle = new SegmentHandle(Provider, oldIndex - shift);
                        model.RefreshDisplayName();
                    }
                }
            }

            // Record Undo for the provider
            Undo.RecordObject(Provider, "Delete Segments");
            
            // Remove the actual segments from the Provider
            var seqSegments = Provider.Sequence.Segments;
            for (int i = seqSegments.Count - 1; i >= 0; i--)
            {
                if (targetsToDelete.Contains(seqSegments[i]))
                {
                    seqSegments.RemoveAt(i);
                }
            }

            // Destroy the ScriptableObjects using Unity's Undo so it's fully recorded
            foreach (var model in segmentModels)
            {
                if (model != null)
                {
                    Undo.DestroyObjectImmediate(model);
                }
            }

            EditorUtility.SetDirty(Provider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(Provider);

            Refresh();
        }

        public List<Segment> TryPaste(IReadOnlyList<SegmentTimelineWindow.ClipboardEntry> clipboard, float displayTime, bool isPreviewing)
        {
            if (Provider == null || Provider.Sequence == null || clipboard == null || clipboard.Count == 0)
                return null;

            float earliestStart = float.MaxValue;
            float latestEnd = float.MinValue;
            for (int i = 0; i < clipboard.Count; i++)
            {
                earliestStart = Mathf.Min(earliestStart, clipboard[i].StartTime);
                latestEnd = Mathf.Max(latestEnd, clipboard[i].EndTime);
            }
            float clipboardDuration = latestEnd - earliestStart;
            float pasteOrigin = isPreviewing
                ? displayTime - clipboardDuration
                : earliestStart;

            Undo.RecordObject(Provider, "Paste Segments");
            var pasted = new List<Segment>();

            for (int i = 0; i < clipboard.Count; i++)
            {
                var entry = clipboard[i];
                var type = Type.GetType(entry.TypeName);
                if (type == null)
                    continue;

                Segment segment;
                try { segment = (Segment)JsonUtility.FromJson(entry.Json, type); }
                catch { continue; }

                float newStart = pasteOrigin + (entry.StartTime - earliestStart);
                if (segment is IStartTimeConfigurable timeConfig)
                    timeConfig.SetStartTime(Mathf.Max(0f, newStart));

                Provider.Sequence.Segments.Add(segment);
                pasted.Add(segment);
            }

            if (pasted.Count > 0)
            {
                EditorUtility.SetDirty(Provider);
                PrefabUtility.RecordPrefabInstancePropertyModifications(Provider);
                Refresh();
            }

            return pasted;
        }

        public void ClearModels()
        {
            for (int i = 0; i < Models.Count; i++)
            {
                if (Models[i] != null)
                    UnityEngine.Object.DestroyImmediate(Models[i]);
            }
            Models.Clear();
        }

        public void Dispose()
        {
            ClearModels();
        }
    }
}
