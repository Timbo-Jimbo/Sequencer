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
        public string SequenceName { get; private set; }
        public Sequence ActiveSequence => TryResolveSequence(Provider, SequenceName, out var sequence, out _) ? sequence : null;
        public List<SegmentSelectionModel> Models { get; } = new();

        public event Action SessionRefreshed;

        public void Bind(SequenceProvider provider, string sequenceName = null)
        {
            Provider = provider;
            SequenceName = ResolveValidSequenceName(provider, sequenceName);
            Refresh();
        }

        public void SetSequenceName(string sequenceName)
        {
            SequenceName = ResolveValidSequenceName(Provider, sequenceName);
            Refresh();
        }

        public static bool TryResolveSequence(SequenceProvider provider, string sequenceName, out Sequence sequence, out int sequenceIndex)
        {
            sequence = null;
            sequenceIndex = -1;

            if (provider == null || provider.Sequences == null || provider.Sequences.Count == 0)
                return false;

            for (int i = 0; i < provider.Sequences.Count; i++)
            {
                var candidate = provider.Sequences[i];
                if (candidate == null)
                    continue;

                if (string.Equals(candidate.Name, sequenceName, StringComparison.Ordinal))
                {
                    sequence = candidate;
                    sequenceIndex = i;
                    return true;
                }
            }

            return false;
        }

        public static string ResolveValidSequenceName(SequenceProvider provider, string preferredName)
        {
            if (provider == null || provider.Sequences == null || provider.Sequences.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(preferredName) && TryResolveSequence(provider, preferredName, out _, out _))
                return preferredName;

            for (int i = 0; i < provider.Sequences.Count; i++)
            {
                if (provider.Sequences[i] != null)
                    return provider.Sequences[i].Name;
            }

            return null;
        }

        public static string MakeUniqueSequenceName(IReadOnlyList<Sequence> sequences, string seed)
        {
            string baseName = string.IsNullOrWhiteSpace(seed) ? "Sequence" : seed.Trim();
            var taken = new HashSet<string>(StringComparer.Ordinal);

            if (sequences != null)
            {
                for (int i = 0; i < sequences.Count; i++)
                {
                    if (sequences[i] == null)
                        continue;

                    taken.Add(sequences[i].Name ?? string.Empty);
                }
            }

            if (!taken.Contains(baseName))
                return baseName;

            int suffix = 2;
            while (taken.Contains($"{baseName} {suffix}"))
                suffix++;

            return $"{baseName} {suffix}";
        }

        public void Refresh()
        {
            if (!TryResolveSequence(Provider, SequenceName, out var activeSequence, out _))
            {
                ClearModels();
                SessionRefreshed?.Invoke();
                return;
            }

            var activeLayer = activeSequence.Segments;
            var existingByIndex = new Dictionary<int, SegmentSelectionModel>();

            for (int i = 0; i < Models.Count; i++)
            {
                var model = Models[i];
                if (model != null
                    && ReferenceEquals(model.Handle.Provider, Provider)
                    && string.Equals(model.Handle.SequenceName, SequenceName, StringComparison.Ordinal))
                {
                    existingByIndex[model.Handle.Index] = model;
                }
            }

            var currentSelections = Selection.objects.OfType<SegmentSelectionModel>();
            foreach (var model in currentSelections)
            {
                if (model != null
                    && ReferenceEquals(model.Handle.Provider, Provider)
                    && string.Equals(model.Handle.SequenceName, SequenceName, StringComparison.Ordinal))
                {
                    existingByIndex[model.Handle.Index] = model;
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
                    reused.Bind(Provider, SequenceName, segment, i);
                    nextModels.Add(reused);
                    existingByIndex.Remove(i);
                }
                else
                {
                    var model = ScriptableObject.CreateInstance<SegmentSelectionModel>();
                    model.hideFlags = HideFlags.DontSave;
                    model.Bind(Provider, SequenceName, segment, i);
                    nextModels.Add(model);
                }
            }

            foreach (var stale in existingByIndex.Values)
            {
                if (stale != null)
                    UnityEngine.Object.DestroyImmediate(stale);
            }

            Models.Clear();
            Models.AddRange(nextModels);

            SessionRefreshed?.Invoke();
        }

        public static void CommitChanges(IReadOnlyList<SegmentSelectionModel> segmentModels)
        {
            if (segmentModels == null || segmentModels.Count == 0)
                return;

            var uniqueProviders = new HashSet<SequenceProvider>();

            var groups = segmentModels
                .Where(m => m != null && m.Handle.Provider != null && !string.IsNullOrWhiteSpace(m.Handle.SequenceName))
                .GroupBy(m => (provider: m.Handle.Provider, sequenceName: m.Handle.SequenceName));

            foreach (var group in groups)
            {
                var provider = group.Key.provider;
                var sequenceName = group.Key.sequenceName;

                if (!TryResolveSequence(provider, sequenceName, out var sequence, out _))
                    continue;

                var segments = sequence.Segments;
                Undo.RecordObject(provider, "Edit Segment");

                bool changesApplied = false;

                foreach (var model in group)
                {
                    int index = model.Handle.Index;
                    if (index < 0 || index >= segments.Count)
                        continue;

                    var existing = segments[index];
                    if (existing != null && existing.GetType() == model.Segment.GetType())
                    {
                        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(model.Segment), existing);
                    }
                    else
                    {
                        segments[index] = CloneSegment(model.Segment);
                    }

                    changesApplied = true;
                }

                if (changesApplied)
                {
                    uniqueProviders.Add(provider);
                    EditorUtility.SetDirty(provider);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(provider);
                }
            }

            var windows = Resources.FindObjectsOfTypeAll<SegmentTimelineWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                var window = windows[i];
                if (window != null && uniqueProviders.Contains(window.Provider))
                    window.RefreshPlan();
            }
        }

        private static Segment CloneSegment(Segment source)
        {
            if (source == null)
                return null;

            return JsonUtility.FromJson(JsonUtility.ToJson(source), source.GetType()) as Segment;
        }

        public void UpdateProxyTimings(IReadOnlyList<(SegmentSelectionModel model, float startTime, float duration)> timingChanges)
        {
            if (timingChanges == null || timingChanges.Count == 0)
                return;

            var proxiesToCommit = new List<SegmentSelectionModel>();
            for (int i = 0; i < timingChanges.Count; i++)
            {
                var change = timingChanges[i];
                if (change.model == null)
                    continue;

                change.model.StartTime = change.startTime;
                change.model.Duration = change.duration;
                proxiesToCommit.Add(change.model);
            }

            CommitChanges(proxiesToCommit);
        }

        public void StackSegmentsEndToEnd(IReadOnlyList<SegmentSelectionModel> segmentModels)
        {
            var orderedModels = GetOrderedSelectedModels(segmentModels);
            if (orderedModels.Count < 2)
                return;

            float nextStartTime = orderedModels[0].StartTime;
            var modelsToCommit = new List<SegmentSelectionModel>(orderedModels.Count);

            for (int i = 0; i < orderedModels.Count; i++)
            {
                var model = orderedModels[i];
                if (!model.CanAdjustStartTime)
                {
                    nextStartTime = Mathf.Max(nextStartTime, model.EndTime);
                    continue;
                }

                model.StartTime = nextStartTime;
                modelsToCommit.Add(model);
                nextStartTime = model.EndTime;
            }

            CommitChanges(modelsToCommit);
        }

        public void AlignSegmentsByStart(IReadOnlyList<SegmentSelectionModel> segmentModels)
        {
            var orderedModels = GetOrderedSelectedModels(segmentModels);
            if (orderedModels.Count < 2)
                return;

            float anchorStart = orderedModels[0].StartTime;
            CommitAlignedStartTimes(orderedModels, _ => anchorStart);
        }

        public void AlignSegmentsByEnd(IReadOnlyList<SegmentSelectionModel> segmentModels)
        {
            var orderedModels = GetOrderedSelectedModels(segmentModels);
            if (orderedModels.Count < 2)
                return;

            float anchorEnd = orderedModels.Max(m => m.EndTime);
            CommitAlignedStartTimes(orderedModels, model => anchorEnd - model.Duration);
        }

        private List<SegmentSelectionModel> GetOrderedSelectedModels(IReadOnlyList<SegmentSelectionModel> segmentModels)
        {
            if (segmentModels == null || segmentModels.Count < 2)
                return new List<SegmentSelectionModel>();

            return segmentModels
                .Where(m => m != null
                            && ReferenceEquals(m.Handle.Provider, Provider)
                            && string.Equals(m.Handle.SequenceName, SequenceName, StringComparison.Ordinal))
                .GroupBy(m => m.Handle.Index)
                .Select(g => g.First())
                .OrderBy(m => m.StartTime)
                .ThenBy(m => m.Handle.Index)
                .ToList();
        }

        private static void CommitAlignedStartTimes(IReadOnlyList<SegmentSelectionModel> orderedModels, Func<SegmentSelectionModel, float> getTargetStartTime)
        {
            var modelsToCommit = new List<SegmentSelectionModel>(orderedModels.Count);
            for (int i = 0; i < orderedModels.Count; i++)
            {
                var model = orderedModels[i];
                if (!model.CanAdjustStartTime)
                    continue;

                model.StartTime = getTargetStartTime(model);
                modelsToCommit.Add(model);
            }

            CommitChanges(modelsToCommit);
        }

        public void AddSegment(Type type, float time)
        {
            if (Provider == null || ActiveSequence == null)
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
            ActiveSequence.Segments.Add(created);

            EditorUtility.SetDirty(Provider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(Provider);

            Refresh();
        }

        public void AddSegment(Segment segment)
        {
            if (Provider == null || ActiveSequence == null || segment == null)
                return;

            var cloned = CloneSegment(segment);
            if (cloned == null)
                return;

            Undo.RecordObject(Provider, $"Add {cloned.GetType().Name}");
            ActiveSequence.Segments.Add(cloned);

            EditorUtility.SetDirty(Provider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(Provider);

            Refresh();
        }

        public List<Segment> ConvertSegments(IReadOnlyList<SegmentSelectionModel> segmentModels, Converters.SegmentConverter converter)
        {
            if (Provider == null || ActiveSequence == null || converter == null || segmentModels == null || segmentModels.Count == 0)
                return null;

            var validModels = segmentModels
                .Where(m => m != null
                            && ReferenceEquals(m.Handle.Provider, Provider)
                            && string.Equals(m.Handle.SequenceName, SequenceName, StringComparison.Ordinal))
                .GroupBy(m => m.Handle.Index)
                .Select(g => g.First())
                .OrderBy(m => m.Handle.Index)
                .ToList();

            if (validModels.Count == 0)
                return null;

            var inputSegments = validModels.Select(m => m.Segment).ToList();

            List<Segment> outputSegments;
            try
            {
                outputSegments = converter.Convert(inputSegments);
            }
            catch (Exception e)
            {
                Debug.LogError($"Segment conversion '{converter.MenuName}' failed: {e.Message}");
                return null;
            }

            if (outputSegments == null || outputSegments.Count == 0)
                return null;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"Convert Segments ({converter.MenuName})");
            int undoGroup = Undo.GetCurrentGroup();

            DeleteSegments(validModels);

            var inserted = new List<Segment>();
            foreach (var segment in outputSegments)
            {
                if (segment == null)
                    continue;

                AddSegment(segment);

                var segments = ActiveSequence?.Segments;
                if (segments != null && segments.Count > 0)
                    inserted.Add(segments[segments.Count - 1]);
            }

            Undo.CollapseUndoOperations(undoGroup);

            return inserted;
        }

        public void DeleteSegments(IReadOnlyList<SegmentSelectionModel> segmentModels)
        {
            if (Provider == null || ActiveSequence == null || segmentModels == null || segmentModels.Count == 0)
                return;

            List<int> deletedIndices = new List<int>();
            HashSet<Segment> targetsToDelete = new HashSet<Segment>();
            var activeSegments = ActiveSequence.Segments;

            foreach (var model in segmentModels)
            {
                if (model == null)
                    continue;

                if (!ReferenceEquals(model.Handle.Provider, Provider)
                    || !string.Equals(model.Handle.SequenceName, SequenceName, StringComparison.Ordinal))
                    continue;

                int index = model.Handle.Index;
                if (index >= 0 && index < activeSegments.Count)
                {
                    deletedIndices.Add(index);
                    targetsToDelete.Add(activeSegments[index]);
                }
            }

            if (targetsToDelete.Count == 0)
                return;

            deletedIndices.Sort();

            foreach (var model in Models)
            {
                if (model == null)
                    continue;

                int oldIndex = model.Handle.Index;
                if (deletedIndices.Contains(oldIndex))
                    continue;

                int shift = 0;
                foreach (int deletedIdx in deletedIndices)
                {
                    if (deletedIdx < oldIndex)
                        shift++;
                }

                if (shift > 0)
                {
                    Undo.RecordObject(model, "Delete Segments");
                    model.Handle = new SegmentHandle(Provider, SequenceName, oldIndex - shift);
                    model.RefreshDisplayName();
                }
            }

            Undo.RecordObject(Provider, "Delete Segments");

            for (int i = activeSegments.Count - 1; i >= 0; i--)
            {
                if (targetsToDelete.Contains(activeSegments[i]))
                    activeSegments.RemoveAt(i);
            }

            foreach (var model in segmentModels)
            {
                if (model != null)
                    Undo.DestroyObjectImmediate(model);
            }

            EditorUtility.SetDirty(Provider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(Provider);

            Refresh();
        }

        public List<Segment> TryPaste(IReadOnlyList<SegmentTimelineWindow.ClipboardEntry> clipboard, float displayTime, bool isPreviewing)
        {
            if (Provider == null || ActiveSequence == null || clipboard == null || clipboard.Count == 0)
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
                try
                {
                    segment = (Segment)JsonUtility.FromJson(entry.Json, type);
                }
                catch
                {
                    continue;
                }

                float newStart = pasteOrigin + (entry.StartTime - earliestStart);
                if (segment is IStartTimeConfigurable timeConfig)
                    timeConfig.SetStartTime(Mathf.Max(0f, newStart));

                ActiveSequence.Segments.Add(segment);
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
