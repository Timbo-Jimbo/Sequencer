using System;
using System.Collections.Generic;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using TimboJimboEditor.Sequencer.Blocks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimboJimboEditor.Sequencer
{
    [CustomEditor(typeof(SequenceProvider))]
    public sealed class SequenceSegmentProviderEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement
            {
                style =
                {
                    paddingLeft = 2f,
                    paddingRight = 2f,
                    paddingTop = 2f,
                    paddingBottom = 2f,
                }
            };

            RebuildRoot(root);
            return root;
        }

        private void RebuildRoot(VisualElement root)
        {
            root.Clear();

            if (targets.Length > 1)
            {
                root.Add(new HelpBox("Multi-object editing is not supported for sequence management.", HelpBoxMessageType.Info));
                return;
            }

            var provider = (SequenceProvider)target;
            if (provider == null)
                return;

            provider.Sequences ??= new List<Sequence>();

            var scriptField = new ObjectField("Script")
            {
                value = MonoScript.FromMonoBehaviour(provider),
                objectType = typeof(MonoScript),
                allowSceneObjects = false,
            };
            scriptField.SetEnabled(false);
            root.Add(scriptField);

            var headerRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginTop = 6f,
                    marginBottom = 4f,
                }
            };
            headerRow.Add(new Label("Sequences")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    flexGrow = 1f,
                }
            });
            headerRow.Add(new Button(() =>
            {
                AddSequence(provider);
                RebuildRoot(root);
            }) { text = "Add" });
            root.Add(headerRow);

            if (provider.Sequences.Count == 0)
            {
                root.Add(new HelpBox("No sequences yet. Add one to start editing in the timeline.", HelpBoxMessageType.None));
            }

            for (int i = 0; i < provider.Sequences.Count; i++)
                root.Add(CreateSequenceRow(root, provider, i));

            root.Add(new VisualElement { style = { height = 6f } });
            root.Add(new Button(() => SegmentTimelineWindow.Open(provider)) { text = "Open Timeline" });
        }

        private VisualElement CreateSequenceRow(VisualElement root, SequenceProvider provider, int index)
        {
            var container = new VisualElement
            {
                style =
                {
                    marginBottom = 6f,
                    paddingLeft = 6f,
                    paddingRight = 6f,
                    paddingTop = 6f,
                    paddingBottom = 6f,
                    borderTopWidth = 1f,
                    borderBottomWidth = 1f,
                    borderLeftWidth = 1f,
                    borderRightWidth = 1f,
                    borderTopColor = new Color(0.25f, 0.25f, 0.25f),
                    borderBottomColor = new Color(0.25f, 0.25f, 0.25f),
                    borderLeftColor = new Color(0.25f, 0.25f, 0.25f),
                    borderRightColor = new Color(0.25f, 0.25f, 0.25f),
                    borderTopLeftRadius = 3f,
                    borderTopRightRadius = 3f,
                    borderBottomLeftRadius = 3f,
                    borderBottomRightRadius = 3f,
                }
            };

            if (index < 0 || index >= provider.Sequences.Count)
                return container;

            var sequence = provider.Sequences[index];
            if (sequence == null)
            {
                container.Add(new Label("(Missing sequence)") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
                container.Add(new Button(() =>
                {
                    Undo.RecordObject(provider, "Remove Sequence");
                    provider.Sequences.RemoveAt(index);
                    MarkProviderDirty(provider);
                    RebuildRoot(root);
                }) { text = "Remove" });
                return container;
            }

            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 6f,
                }
            };

            var nameField = new TextField
            {
                value = sequence.Name ?? string.Empty,
                isDelayed = true,
                style = { flexGrow = 1f, marginRight = 6f }
            };
            nameField.RegisterValueChangedCallback(evt =>
            {
                string currentName = sequence.Name ?? string.Empty;
                if (string.Equals(currentName, evt.newValue, StringComparison.Ordinal))
                    return;

                RenameSequence(provider, sequence, evt.newValue);
                RebuildRoot(root);
            });
            row.Add(nameField);

            row.Add(new Button(() => SegmentTimelineWindow.Open(provider, sequence.Name))
            {
                text = "Edit",
                style = { width = 52f, marginRight = 4f }
            });

            var menuButton = new Button(() => ShowSequenceMenu(provider, index, () => RebuildRoot(root)))
            {
                text = "⋮",
                style = { width = 24f }
            };
            row.Add(menuButton);

            container.Add(row);

            var preview = new VisualElement
            {
                style =
                {
                    position = Position.Relative,
                    height = 46f,
                    borderTopWidth = 1f,
                    borderBottomWidth = 1f,
                    borderLeftWidth = 1f,
                    borderRightWidth = 1f,
                    borderTopLeftRadius = 3f,
                    borderTopRightRadius = 3f,
                    borderBottomLeftRadius = 3f,
                    borderBottomRightRadius = 3f,
                    paddingTop = 1f,
                    paddingBottom = 1f,
                    paddingLeft = 1f,
                    paddingRight = 1f,
                    overflow = Overflow.Hidden,
                    backgroundColor = new Color(0.122f, 0.122f, 0.122f, 0.75f),
                }
            };

            preview.style.borderTopColor = new Color(0.24f, 0.24f, 0.24f);
            preview.style.borderBottomColor = new Color(0.24f, 0.24f, 0.24f);
            preview.style.borderLeftColor = new Color(0.24f, 0.24f, 0.24f);
            preview.style.borderRightColor = new Color(0.24f, 0.24f, 0.24f);

            BuildMiniTimelinePreview(sequence, preview);

            container.Add(preview);
            return container;
        }

        private static void BuildMiniTimelinePreview(Sequence sequence, VisualElement preview)
        {
            if (sequence == null || preview == null || sequence.Segments == null || sequence.Segments.Count == 0)
                return;

            var entries = new List<PreviewEntry>();
            float maxEnd = 0f;

            for (int i = 0; i < sequence.Segments.Count; i++)
            {
                var segment = sequence.Segments[i];
                if (segment == null)
                    continue;

                var plan = segment.GetPlan(null);
                if (plan == null)
                    continue;

                float start = Mathf.Max(0f, plan.Timing.AbsoluteStartTime);
                float duration = Mathf.Max(0f, plan.Timing.AbsoluteDuration);
                float end = Mathf.Max(start, start + duration);
                maxEnd = Mathf.Max(maxEnd, end);

                entries.Add(new PreviewEntry(segment, start, end));
            }

            if (entries.Count == 0)
                return;

            var packed = LanePacker.Pack(
                items: entries,
                itemToInput: entry => new LanePacker.PackInput<PreviewEntry>
                {
                    Data = entry,
                    Group = SegmentBlockEditorRegistry.GetEditor(entry.Segment).GetLanePackerGroup(entry.Segment),
                    Start = entry.Start,
                    End = Mathf.Max(entry.End, entry.Start + 0.001f),
                },
                depenetrateAndCompact: true);

            int laneCount = 1;
            for (int i = 0; i < packed.Count; i++)
                laneCount = Mathf.Max(laneCount, packed[i].Lane + 1);

            float timelineDuration = Mathf.Max(0.1f, maxEnd);

            for (int i = 0; i < packed.Count; i++)
            {
                var pair = packed[i];
                var entry = pair.Item;
                var editor = SegmentBlockEditorRegistry.GetEditor(entry.Segment);
                var (fill, border) = editor.GetBlockColors(entry.Segment);
                border.a = 0.25f;

                float duration = Mathf.Max(0f, entry.End - entry.Start);
                float leftPercent = Mathf.Clamp01(entry.Start / timelineDuration) * 100f;
                float widthPercent = Mathf.Clamp01(duration / timelineDuration) * 100f;
                float topPercent = (pair.Lane / (float)laneCount) * 100f;
                float laneHeightPercent = (1f / laneCount) * 100f;

                var blockContainer = new VisualElement
                {
                    style =
                    {
                        position = Position.Absolute,
                        left = Length.Percent(leftPercent),
                        top = Length.Percent(topPercent),
                        width = Length.Percent(widthPercent),
                        minWidth = 6f,
                        height = Length.Percent(laneHeightPercent),
                    },
                    tooltip = ObjectNames.NicifyVariableName(entry.Segment.GetType().Name),
                };

                var blockVisual = new VisualElement
                {
                    style =
                    {
                        position = Position.Absolute,
                        left = 0.5f,
                        top = 0.5f,
                        right = 0.5f,
                        bottom = 0.5f,
                        backgroundColor = fill,
                        borderTopWidth = 1f,
                        borderBottomWidth = 1f,
                        borderLeftWidth = 1f,
                        borderRightWidth = 1f,
                        borderTopColor = border,
                        borderBottomColor = border,
                        borderLeftColor = border,
                        borderRightColor = border,
                        borderTopLeftRadius = 2f,
                        borderTopRightRadius = 2f,
                        borderBottomLeftRadius = 2f,
                        borderBottomRightRadius = 2f,
                    }
                };

                blockContainer.Add(blockVisual);

                preview.Add(blockContainer);
            }
        }

        private readonly struct PreviewEntry
        {
            public readonly Segment Segment;
            public readonly float Start;
            public readonly float End;

            public PreviewEntry(Segment segment, float start, float end)
            {
                Segment = segment;
                Start = start;
                End = end;
            }
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

        private static void ShowSequenceMenu(SequenceProvider provider, int index, Action onChanged)
        {
            var menu = new GenericMenu();

            if (index >= 0 && index < provider.Sequences.Count && provider.Sequences[index] != null)
            {
                var sequence = provider.Sequences[index];
                menu.AddItem(new GUIContent("Edit in Timeline"), false, () => SegmentTimelineWindow.Open(provider, sequence.Name));
                menu.AddSeparator(string.Empty);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Edit in Timeline"));
                menu.AddSeparator(string.Empty);
            }

            menu.AddItem(new GUIContent("Duplicate"), false, () =>
            {
                DuplicateSequence(provider, index);
                onChanged?.Invoke();
            });
            menu.AddItem(new GUIContent("Remove"), false, () =>
            {
                RemoveSequence(provider, index);
                onChanged?.Invoke();
            });
            menu.ShowAsContext();
        }

        private static void DuplicateSequence(SequenceProvider provider, int index)
        {
            if (index < 0 || index >= provider.Sequences.Count)
                return;

            var source = provider.Sequences[index];
            if (source == null)
                return;

            Undo.RecordObject(provider, "Duplicate Sequence");

            string baseName = string.IsNullOrWhiteSpace(source.Name) ? "Sequence" : source.Name;
            string duplicateName = TimelineSessionState.MakeUniqueSequenceName(provider.Sequences, $"{baseName} Copy");

            var duplicate = JsonUtility.FromJson<Sequence>(JsonUtility.ToJson(source));
            duplicate.Name = duplicateName;

            provider.Sequences.Insert(index + 1, duplicate);
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
