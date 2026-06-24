using System.Collections.Generic;
using System.Linq;
using TimboJimbo.Sequencer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimboJimboEditor.Sequencer
{
    [CustomEditor(typeof(SegmentSelectionModel)), CanEditMultipleObjects]
    public sealed class SegmentSelectionModelEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var allModels = targets.OfType<SegmentSelectionModel>()
                .Where(m => m != null && m.Segment != null)
                .ToList();

            if (allModels.Count == 1)
            {
                var model = allModels[0];
                var blockEditor = SegmentBlockEditorRegistry.GetEditor(model.Segment);

                var previewBlock = new VisualElement
                {
                    style =
                    {
                        marginTop = 4f,
                        marginBottom = 8f,
                    }
                };

                blockEditor.OnBlockGUI(model.Segment, previewBlock);
                root.Add(previewBlock);
            }
            else if (allModels.Count > 1)
            {
                var multiHeader = new VisualElement
                {
                    style =
                    {
                        marginTop = 4f,
                        marginBottom = 8f,
                    }
                };

                multiHeader.Add(new Label($"{allModels.Count} Segments Selected")
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Bold,
                        marginBottom = 6f,
                    }
                });

                var grouped = allModels
                    .GroupBy(m => m.Segment.GetType())
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key.Name);

                foreach (var group in grouped)
                {
                    var representative = group.First().Segment;
                    var blockEditor = SegmentBlockEditorRegistry.GetEditor(representative);
                    var (fill, border) = blockEditor.GetBlockColors(representative);

                    var row = new VisualElement
                    {
                        style =
                        {
                            flexDirection = FlexDirection.Row,
                            alignItems = Align.Center,
                            marginBottom = 4f,
                        }
                    };

                    var swatch = new VisualElement
                    {
                        style =
                        {
                            width = 12f,
                            height = 12f,
                            marginRight = 6f,
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
                    row.Add(swatch);

                    row.Add(new Label($"{group.Count()} {ObjectNames.NicifyVariableName(group.Key.Name)}")
                    {
                        style =
                        {
                            unityFontStyleAndWeight = FontStyle.Normal,
                        }
                    });

                    multiHeader.Add(row);
                }

                root.Add(multiHeader);
            }

            root.Add(new IMGUIContainer(DrawInspectorBody));
            return root;
        }

        protected override void OnHeaderGUI()
        {
            // Intentionally blank: header is rendered in CreateInspectorGUI.
        }

        public override void OnInspectorGUI() => DrawInspectorBody();

        private void DrawInspectorBody()
        {
            var allModels = targets.OfType<SegmentSelectionModel>()
                .Where(m => m != null && m.Segment != null && m.Handle.Provider != null)
                .ToList();

            if (allModels.Count == 0)
            {
                EditorGUILayout.HelpBox("No segment selection models are active.", MessageType.Info);
                return;
            }

            var grouped = allModels.GroupBy(m => m.Segment.GetType());
            foreach (var group in grouped)
            {
                if (grouped.Count() > 1)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(group.Key.Name), EditorStyles.boldLabel);
                }

                var groupModels = group.ToList();
                var groupObjects = groupModels.Cast<UnityEngine.Object>().ToArray();
                using var groupSo = new SerializedObject(groupObjects);
                groupSo.Update();

                var segmentProp = groupSo.FindProperty("Segment");
                var segmentInspector = SegmentInspectorRegistry.GetInspectorByType(group.Key);
                segmentInspector.OnInspectorGUI(segmentProp);

                if (!groupSo.ApplyModifiedProperties())
                    continue;
                SyncModelsBackToProviders(groupModels);
            }
        }

        private static void SyncModelsBackToProviders(IReadOnlyList<SegmentSelectionModel> models)
        {
            for (int i = 0; i < models.Count; i++)
            {
                models[i]?.CommitToProvider();
            }
        }
    }
}
