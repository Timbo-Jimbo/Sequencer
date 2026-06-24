using System.Collections.Generic;
using System.Linq;
using TimboJimbo.Sequencer;
using TimboJimboEditor.Sequencer.Blocks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimboJimboEditor.Sequencer
{
    [CustomEditor(typeof(SegmentSelectionModel)), CanEditMultipleObjects]
    public sealed class SegmentSelectionModelEditor : Editor
    {
        private readonly List<PreviewBinding> _previewBindings = new();

        private sealed class PreviewBinding
        {
            public SegmentSelectionModel Model;
            public VisualElement PreviewElement;
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += RefreshAllPreviews;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshAllPreviews;
            _previewBindings.Clear();
        }

        public override VisualElement CreateInspectorGUI()
        {
            _previewBindings.Clear();

            var root = new VisualElement();

            var allModels = targets
                .OfType<SegmentSelectionModel>()
                .Where(m => m != null && m.Segment != null)
                .ToList();

            if (allModels.Count == 0)
                return root;

            var grouped = allModels
                .GroupBy(m => m.Segment.GetType())
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.Name)
                .ToList();

            for (int groupIndex = 0; groupIndex < grouped.Count; groupIndex++)
            {
                var group = grouped[groupIndex];
                var groupModels = group.ToList();

                var typeLabel = ObjectNames.NicifyVariableName(group.Key.Name);
                if (groupModels.Count > 1)
                    typeLabel += "s";

                var countLabel = groupModels.Count > 1 ? $"{groupModels.Count}x " : "";

                root.Add(new Label($"{countLabel}{typeLabel}")
                {
                    style =
                    {
                        marginTop = groupIndex > 0 ? 12f : 4f,
                        marginBottom = 6f,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        fontSize = 12,
                    }
                });

                var previewContainer = new VisualElement
                {
                    style =
                    {
                        marginBottom = 8f,
                        position = Position.Relative,
                        height = 56f,
                        width = new Length(100, LengthUnit.Percent),
                    }
                };

                var groupPreviewBindings = new List<PreviewBinding>(groupModels.Count);

                for (int i = 0; i < groupModels.Count; i++)
                {
                    var model = groupModels[i];

                    var wrapper = new VisualElement
                    {
                        style =
                        {
                            position = Position.Absolute,
                            left = i * 32f,
                            top = 0f,
                            right = 0f,
                            height = 48f,
                            backgroundColor = new Color(0.219f, 0.219f, 0.219f, 0f),
                            borderTopLeftRadius = 4f,
                            borderTopRightRadius = 4f,
                            borderBottomLeftRadius = 4f,
                            borderBottomRightRadius = 4f,
                            
                        }
                    };

                    var blockPreview = new VisualElement
                    {
                        style =
                        {
                            position = Position.Relative,
                            width = new Length(100, LengthUnit.Percent),
                            height = 48f,
                            borderTopWidth = 1f,
                            borderBottomWidth = 1f,
                            borderLeftWidth = 1f,
                            borderRightWidth = 1f,
                            borderTopLeftRadius = 4f,
                            borderTopRightRadius = 4f,
                            borderBottomLeftRadius = 4f,
                            borderBottomRightRadius = 4f,
                            overflow = Overflow.Hidden,
                        }
                    };

                    var previewBinding = new PreviewBinding
                    {
                        Model = model,
                        PreviewElement = blockPreview,
                    };

                    groupPreviewBindings.Add(previewBinding);
                    _previewBindings.Add(previewBinding);

                    RefreshPreview(previewBinding);

                    if (i > 0)
                    {
                        wrapper.Add(CreateOverlapSeamShadow());
                    }

                    wrapper.Add(blockPreview);
                    previewContainer.Add(wrapper);
                }

                root.Add(previewContainer);
                root.Add(new IMGUIContainer(() => DrawGroupInspector(groupModels, groupPreviewBindings)));
            }

            return root;
        }

        protected override void OnHeaderGUI()
        {
            // Intentionally blank: header is rendered in CreateInspectorGUI.
        }

        private void DrawGroupInspector(IReadOnlyList<SegmentSelectionModel> groupModels, IReadOnlyList<PreviewBinding> previewBindings)
        {
            if (groupModels.Count == 0)
                return;

            var groupType = groupModels[0].Segment.GetType();
            var groupObjects = groupModels.Cast<UnityEngine.Object>().ToArray();

            using var groupSo = new SerializedObject(groupObjects);
            groupSo.Update();

            var segmentProp = groupSo.FindProperty("Segment");
            var segmentInspector = SegmentInspectorRegistry.GetInspectorByType(groupType);
            segmentInspector.OnInspectorGUI(segmentProp);

            if (groupSo.ApplyModifiedProperties())
            {
                TimelineSessionState.CommitChanges(groupModels);
                RefreshPreviews(previewBindings);
            }
        }

        private void RefreshAllPreviews()
        {
            RefreshPreviews(_previewBindings);
            Repaint();
        }

        private static void RefreshPreviews(IReadOnlyList<PreviewBinding> previewBindings)
        {
            for (int i = 0; i < previewBindings.Count; i++)
            {
                RefreshPreview(previewBindings[i]);
            }
        }

        private static void RefreshPreview(PreviewBinding binding)
        {
            if (binding?.PreviewElement == null)
                return;

            var blockPreview = binding.PreviewElement;
            blockPreview.Clear();

            var segment = binding.Model?.Segment;
            if (segment == null)
                return;

            var blockEditor = SegmentBlockEditorRegistry.GetEditor(segment);
            var (fill, border) = blockEditor.GetBlockColors(segment);

            blockPreview.style.backgroundColor = fill;
            blockPreview.style.borderTopColor = border;
            blockPreview.style.borderBottomColor = border;
            blockPreview.style.borderLeftColor = border;
            blockPreview.style.borderRightColor = border;

            blockEditor.OnBlockGUI(segment, blockPreview);
            blockPreview.MarkDirtyRepaint();
        }

        private static VisualElement CreateOverlapSeamShadow()
        {
            const float shadowWidth = 64f;
            var shadow = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = -shadowWidth,
                    top = 1f,
                    width = shadowWidth,
                    bottom = 1f,
                },
                pickingMode = PickingMode.Ignore,
            };

            shadow.generateVisualContent += mgc => DrawOverlapSeamShadow(mgc, shadow.contentRect.width, shadow.contentRect.height);
            return shadow;
        }

        private static void DrawOverlapSeamShadow(MeshGenerationContext mgc, float width, float height)
        {
            if (width <= 0f || height <= 0f)
                return;

            var mesh = mgc.Allocate(4, 6);
            var farColor = new Color(0f, 0f, 0f, 0f);
            var nearColor = new Color(0.000f, 0.000f, 0.000f, 0.123f);

            mesh.SetNextVertex(new Vertex { position = new Vector3(0f, 0f, Vertex.nearZ), tint = farColor });
            mesh.SetNextVertex(new Vertex { position = new Vector3(width, 0f, Vertex.nearZ), tint = nearColor });
            mesh.SetNextVertex(new Vertex { position = new Vector3(width, height, Vertex.nearZ), tint = nearColor });
            mesh.SetNextVertex(new Vertex { position = new Vector3(0f, height, Vertex.nearZ), tint = farColor });

            mesh.SetNextIndex(0);
            mesh.SetNextIndex(1);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(3);
            mesh.SetNextIndex(0);
        }
    }
}
