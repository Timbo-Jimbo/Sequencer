using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TimboJimbo.Core.Utility;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using TimboJimboEditor.Sequencer.Blocks;
using TimboJimboEditor.Core;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    [CustomEditor(typeof(SequenceProvider))]
    public sealed class SequenceSegmentProviderEditor : Editor
    {
        private static GUIStyle CurrentlyEditingTextStyle;
        private SequenceProvider _provider;
        private readonly Dictionary<Sequence, CachedSequencePreview> _previewCache = new();

        private sealed class CachedSequencePreview
        {
            public int Signature;
            public List<(Rect normalizedRect, Color fill, Color border)> RectsToDraw = new();

            public void Draw(Rect previewRect)
            {
                const float padding = 4f;
                const float laneGap = 0f;
                var verts = new Vector3[4];
                
                previewRect.x = previewRect.x + (padding);
                previewRect.y = previewRect.y + ((padding - laneGap));
                previewRect.width = previewRect.width - (2f * padding);
                previewRect.height = previewRect.height - (2f * (padding - laneGap));

                foreach (var (normalizedRect, fill, border) in RectsToDraw)
                {
                    var drawRect = new Rect(
                        previewRect.x + (normalizedRect.x * previewRect.width),
                        previewRect.y + (normalizedRect.y * previewRect.height),
                        normalizedRect.width * previewRect.width,
                        normalizedRect.height * previewRect.height
                    );

                    drawRect.x = (drawRect.x);
                    drawRect.y = (drawRect.y) + laneGap;
                    drawRect.width = (drawRect.width);
                    drawRect.height = (drawRect.height) - (2f * laneGap);

                    verts[0] = new Vector3(drawRect.xMin, drawRect.yMin);
                    verts[1] = new Vector3(drawRect.xMax, drawRect.yMin);
                    verts[2] = new Vector3(drawRect.xMax, drawRect.yMax);
                    verts[3] = new Vector3(drawRect.xMin, drawRect.yMax);

                    Handles.DrawSolidRectangleWithOutline(verts, fill, border * 0.8f);
                }
            }
        }

        private static bool SequencesFoldoutExpanded
        {
            get => SessionState.GetBool("SequenceSegmentProviderEditor.SequencesFoldoutExpanded", true);
            set => SessionState.SetBool("SequenceSegmentProviderEditor.SequencesFoldoutExpanded", value);
        }

        private void OnEnable()
        {
            _provider = (SequenceProvider)target;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            InvalidatePreviewCache();
        }

        private void OnUndoRedoPerformed()
        {
            InvalidatePreviewCache();
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (targets.Length > 1)
            {
                EditorGUILayout.HelpBox("Multi-object editing is not supported for sequence management.", MessageType.Info);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var provider = _provider != null ? _provider : (SequenceProvider)target;
            if (provider == null)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            provider.Sequences ??= new List<Sequence>();

            FoldoutGUI.Draw(
                expanded: SequencesFoldoutExpanded,
                drawContent: () =>
                {
                    FoldoutGUI.Title("Sequences");
                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                    {
                        if (SequencesEditorGUI.AddButton())
                        {
                            AddSequence(provider);
                            InvalidatePreviewCache();
                            EditorUtility.SetDirty(provider);
                            GUI.FocusControl(null);
                        }
                    }
                },
                onToggle: value => SequencesFoldoutExpanded = value
            );

            if (!SequencesFoldoutExpanded)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing * 2f);

            if (provider.Sequences.Count == 0)
            {
                EditorGUILayout.HelpBox("No sequences yet. Add one to start editing in the timeline.", MessageType.None);
            }

            for (int i = 0; i < provider.Sequences.Count; i++)
            {
                if (DrawSequenceRow(provider, i))
                    break;

                EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing );
            }

            serializedObject.ApplyModifiedProperties();
        }

        private bool DrawSequenceRow(SequenceProvider provider, int index)
        {
            bool requestRebuild = false;

            if (index < 0 || index >= provider.Sequences.Count)
                return false;

            var sequence = provider.Sequences[index];
            bool isMissing = sequence == null;
            bool isOpenInTimeline = !isMissing && SegmentTimelineWindow.IsSequenceContextOpen(provider, sequence.Name);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (isMissing)
                    {
                        EditorGUILayout.LabelField("(Missing sequence)", EditorStyles.boldLabel);
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(isOpenInTimeline))
                        {
                            EditorGUI.BeginChangeCheck();
                            string newName = EditorGUILayout.TextField(sequence.Name ?? string.Empty, GUILayout.ExpandWidth(true));
                            if (EditorGUI.EndChangeCheck())
                            {
                                RenameSequence(provider, sequence, newName);
                                requestRebuild = true;
                            }
                        }
                    }

                    if (isMissing)
                    {
                        if (GUILayout.Button("Remove", GUILayout.Width(64f)))
                        {
                            Undo.RecordObject(provider, "Remove Sequence");
                            provider.Sequences.RemoveAt(index);
                            MarkProviderDirty(provider);
                            return true;
                        }
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(isOpenInTimeline))
                        {
                            SequencesEditorGUI.ButtonGroupButton(
                                content: new GUIContent("Edit"),
                                buttonIndex: 0,
                                buttonCount: 2,
                                onClick: () => SegmentTimelineWindow.Open(provider, sequence.Name),
                                options: GUILayout.Width(64f));

                            if (SequencesEditorGUI.ButtonGroupButton(
                                    content: new GUIContent("✕", "Remove this sequence"),
                                    buttonIndex: 1,
                                    buttonCount: 2,
                                    options: GUILayout.Width(24f)))
                            {
                                RemoveSequence(provider, index);
                                InvalidatePreviewCache();
                                return true;
                            }
                        }
                    }
                }
                DrawMiniTimelinePreview(sequence, isOpenInTimeline);
            }

            if (requestRebuild)
            {
                InvalidatePreviewCache();
                GUI.changed = true;
            }

            return requestRebuild;
        }

        private void DrawMiniTimelinePreview(Sequence sequence, bool isOpenInTimeline)
        {
            if (!TryGetOrBuildCachedPreview(sequence, out var cachedPreview))
                return;

            var previewRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(50), GUILayout.ExpandWidth(true));

            if(Event.current.type != EventType.Repaint)
                return;

            EditorGUI.DrawRect(previewRect, new Color(0.122f, 0.122f, 0.122f, 0.75f));
            
            cachedPreview.Draw(previewRect);

            if (isOpenInTimeline)
            {
                EditorGUI.DrawRect(previewRect, new Color(0.182f, 0.182f, 0.182f, 0.85f));

                if(CurrentlyEditingTextStyle == null)
                {
                    CurrentlyEditingTextStyle = new GUIStyle(EditorStyles.label)
                    {
                        fontStyle = FontStyle.Italic,
                        normal = { textColor = new Color(0.85f, 0.85f, 0.85f, 0.5f) },
                        alignment = TextAnchor.MiddleCenter,
                        clipping = TextClipping.Clip,
                    };
                }
                EditorGUI.LabelField(previewRect, "Currently Editing", CurrentlyEditingTextStyle);
            }
        }

        private bool TryGetOrBuildCachedPreview(Sequence sequence, out CachedSequencePreview preview)
        {
            preview = null;

            if (sequence == null || sequence.Segments == null)
                return false;

            int signature = ComputeSequenceSignature(sequence);

            if (_previewCache.TryGetValue(sequence, out var cached)
                && cached != null
                && cached.Signature == signature)
            {
                preview = cached;
                return true;
            }

            cached = new CachedSequencePreview
            {
                Signature = signature,
            };

            List<(float Start, float End, Segment Segment)> entries = new();

            for (int i = 0; i < sequence.Segments.Count; i++)
            {
                var segment = sequence.Segments[i];
                if (segment == null)
                    continue;

                var plan = segment.GetPlan(null);
                if (plan == null || plan.Timing.AbsoluteDuration <= 0f)
                    continue;

                float start = Mathf.Max(0f, plan.Timing.AbsoluteStartTime);
                float duration = Mathf.Max(0f, plan.Timing.AbsoluteDuration);
                float end = Mathf.Max(start, start + duration);

                entries.Add((start, end, segment));
            }

            //pack
            var packed = LanePacker.Pack(
                items: entries,
                itemToInput: entry => new LanePacker.PackInput<(float Start, float End, Segment Segment)>
                {
                    Data = entry,
                    Group = SegmentBlockEditorRegistry.GetEditor(entry.Segment).GetLanePackerGroup(entry.Segment),
                    Start = entry.Start,
                    End = Mathf.Max(entry.End, entry.Start + 0.001f),
                },
                depenetrateAndCompact: true
            );
            
            //convert packed to normalized draw rects
            var maxEnd = packed.Count > 0 ? packed.Max(x => x.Item.End) : 0f;
            const int minLanes = 4;
            var lanes = packed.Count > 0 ? packed.Max(x => x.Lane) + 1 : 1;
            lanes = Mathf.Max(lanes, minLanes);

            foreach (var pair in packed)
            {
                var entry = pair.Item;
                float duration = Mathf.Max(0f, entry.End - entry.Start);
                float leftPercent = Mathf.Clamp01(entry.Start / maxEnd);
                float widthPercent = Mathf.Clamp01(duration / maxEnd);
                float topPercent = (pair.Lane / (float)lanes);
                float laneHeightPercent = (1f / lanes);

                var normalizedRect = new Rect(leftPercent, topPercent, widthPercent, laneHeightPercent);

                var editor = SegmentBlockEditorRegistry.GetEditor(entry.Segment);
                var (fill, border) = editor.GetBlockColors(entry.Segment);
                {
                    ColorExtra.RGBToOkLCh(border, out float l, out float c, out float h);
                    l *= 0.7f;
                    border = ColorExtra.OkLChToRGB(l, c, h);
                }
                
                cached.RectsToDraw.Add((normalizedRect, fill, border));
            }

            _previewCache[sequence] = cached;
            preview = cached;
            return true;
        }

        private static int ComputeSequenceSignature(Sequence sequence)
        {
            unchecked
            {
                int hash = 17;
                var segments = sequence.Segments;
                hash = hash * 31 + segments.Count;
                for (int i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    if (segment == null)
                    {
                        hash = hash * 31;
                        continue;
                    }

                    hash = hash * 31 + RuntimeHelpers.GetHashCode(segment);
                    hash = hash * 31 + segment.GetHashCode();
                }

                return hash;
            }
        }

        private void InvalidatePreviewCache()
        {
            _previewCache.Clear();
        }

        private static void AddSequence(SequenceProvider provider)
        {
            Undo.RecordObject(provider, "Add Sequence");

            string uniqueName = TimelineSessionState.MakeUniqueSequenceName(provider.Sequences, "Sequence");
            var created = new Sequence
            {
                Name = uniqueName,
                Segments = new List<Segment>()
            };

            provider.Sequences.Add(created);
            MarkProviderDirty(provider);
        }

        private static void RenameSequence(SequenceProvider provider, Sequence sequence, string proposedName)
        {
            if (sequence == null)
                return;

            string trimmed = (proposedName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return;

            string unique = MakeUniqueNameExcluding(provider.Sequences, sequence, trimmed);
            if (string.Equals(sequence.Name, unique, StringComparison.Ordinal))
                return;

            Undo.RecordObject(provider, "Rename Sequence");
            sequence.Name = unique;
            MarkProviderDirty(provider);
        }

        private static void RemoveSequence(SequenceProvider provider, int index)
        {
            if (index < 0 || index >= provider.Sequences.Count)
                return;

            var sequence = provider.Sequences[index];
            string name = sequence != null ? sequence.Name : "(missing)";

            if (!EditorUtility.DisplayDialog("Remove Sequence", $"Remove sequence '{name}'?", "Remove", "Cancel"))
                return;

            Undo.RecordObject(provider, "Remove Sequence");
            provider.Sequences.RemoveAt(index);
            MarkProviderDirty(provider);
        }

        private static string MakeUniqueNameExcluding(IReadOnlyList<Sequence> sequences, Sequence excluded, string seed)
        {
            string baseName = string.IsNullOrWhiteSpace(seed) ? "Sequence" : seed.Trim();
            var taken = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < sequences.Count; i++)
            {
                var candidate = sequences[i];
                if (candidate == null || ReferenceEquals(candidate, excluded))
                    continue;

                taken.Add(candidate.Name ?? string.Empty);
            }

            if (!taken.Contains(baseName))
                return baseName;

            int suffix = 2;
            while (taken.Contains($"{baseName} {suffix}"))
                suffix++;

            return $"{baseName} {suffix}";
        }

        private static void MarkProviderDirty(SequenceProvider provider)
        {
            EditorUtility.SetDirty(provider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(provider);
            SegmentTimelineWindow.NotifyProviderChanged(provider);
        }
    }
}
