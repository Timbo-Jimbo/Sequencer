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
            
            // Build dictionary of current selection models by path
            var existingByPath = new Dictionary<string, SegmentSelectionModel>();
            for (int i = 0; i < Models.Count; i++)
            {
                var m = Models[i];
                if (m != null && !string.IsNullOrEmpty(m.SourcePropertyPath))
                {
                    existingByPath[m.SourcePropertyPath] = m;
                }
            }

            var nextModels = new List<SegmentSelectionModel>();

            for (int i = 0; i < activeLayer.Count; i++)
            {
                var segment = activeLayer[i];
                if (segment == null)
                    continue;

                string path = $"Sequence.Segments.Array.data[{i}]";

                if (existingByPath.TryGetValue(path, out var reused) && reused != null)
                {
                    reused.RefreshFrom(Provider, segment, path);
                    nextModels.Add(reused);
                    existingByPath.Remove(path);
                }
                else
                {
                    var model = ScriptableObject.CreateInstance<SegmentSelectionModel>();
                    model.hideFlags = HideFlags.DontSave;
                    model.InitializeFrom(Provider, segment, path);
                    nextModels.Add(model);
                }
            }

            // Cleanup any models that represent elements which are no longer in the array
            // Note: During an explicit Delete operation, those models were already destroyed via Undo.DestroyObjectImmediate,
            // so they are already null/missing. But other changes (like normal provider syncs) clean up safely here.
            foreach (var stale in existingByPath.Values)
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

            foreach (var model in segmentModels)
            {
                if (model == null) continue;
                int index = GetIndexFromPath(model.SourcePropertyPath);
                if (index >= 0)
                {
                    deletedIndices.Add(index);
                    if (index < Provider.Sequence.Segments.Count)
                    {
                        targetsToDelete.Add(Provider.Sequence.Segments[index]);
                    }
                }
            }

            if (targetsToDelete.Count == 0)
                return;

            // Shift model paths of remaining models before deleting elements
            ShiftModelPathsForDelete(deletedIndices);

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

        private void ShiftModelPathsForDelete(List<int> deletedIndices)
        {
            deletedIndices.Sort((a, b) => b.CompareTo(a)); // Sort descending
            foreach (var deletedIndex in deletedIndices)
            {
                for (int i = 0; i < Models.Count; i++)
                {
                    var model = Models[i];
                    if (model == null) continue;
                    
                    int modelIndex = GetIndexFromPath(model.SourcePropertyPath);
                    if (modelIndex > deletedIndex)
                    {
                        model.SourcePropertyPath = $"Sequence.Segments.Array.data[{modelIndex - 1}]";
                        model.RefreshDisplayName();
                    }
                }
            }
        }

        private int GetIndexFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return -1;
            int openBracket = path.LastIndexOf('[');
            int closeBracket = path.LastIndexOf(']');
            if (openBracket >= 0 && closeBracket > openBracket)
            {
                if (int.TryParse(path.Substring(openBracket + 1, closeBracket - openBracket - 1), out int index))
                    return index;
            }
            return -1;
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
