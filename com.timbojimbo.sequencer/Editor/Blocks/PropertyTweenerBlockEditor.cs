using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimboJimboEditor.Sequencer.Blocks
{
    [CustomSegmentBlockEditor(typeof(PropertyTweener))]
    public sealed class PropertyTweenerBlockEditor : SegmentBlockEditor
    {
        public override void OnBlockGUI(Segment segment, VisualElement block)
        {
            if (segment is not PropertyTweener tweener)
                return;

            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.FlexStart,
                    overflow = Overflow.Hidden,
                    marginLeft = 4f,
                    marginRight = 4f,
                    marginTop = 2f,
                    marginBottom = 2f,
                },
                pickingMode = PickingMode.Ignore,
            };

            var tweenerTargetGo = default(GameObject);
            if (tweener.Property.Target is Component c)
            {
                tweenerTargetGo = c.gameObject;
            }
            else if (tweener.Property.Target is GameObject go)
            {
                tweenerTargetGo = go;
            }

            if (tweenerTargetGo != null)
            {
                var textColumn = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Column,
                        justifyContent = Justify.Center,
                        flexGrow = 1,
                        minWidth = 0,
                        overflow = Overflow.Hidden,
                    },
                    pickingMode = PickingMode.Ignore,
                };

                textColumn.Add(new Label(tweenerTargetGo.name)
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Bold,
                        color = new Color(0.941f, 0.941f, 0.941f, 1.000f),
                        overflow = Overflow.Hidden,
                        marginBottom = 2f,
                    },
                    pickingMode = PickingMode.Ignore,
                });

                var detailRow = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.Center,
                        minWidth = 0,
                        overflow = Overflow.Hidden,
                    },
                    pickingMode = PickingMode.Ignore,
                };

                var propName = NicifiedPropertyNameFromPath(tweener.Property.Path);
                string detailsText;
                {
                    if(string.IsNullOrWhiteSpace(propName))
                    {
                        detailsText = "Select Property...";
                    }
                    else
                    {
                        var typeName = tweener.Property.Target.GetType().Name;
                        detailsText = $"{typeName} > {propName}";

                        var iconContent = EditorGUIUtility.ObjectContent(tweener.Property.Target, tweener.Property.Target.GetType());
                        if (iconContent.image != null)
                        {
                            detailRow.Add(new Image
                            {
                                image = iconContent.image,
                                style =
                                {
                                    width = 14,
                                    height = 14,
                                    marginRight = 3,
                                    flexShrink = 0,
                                },
                                pickingMode = PickingMode.Ignore,
                            });
                        }
                    }
                }

                detailRow.Add(new Label(detailsText)
                {
                    style =
                    {
                        fontSize = 10,
                        color = new Color(0.820f, 0.820f, 0.820f, 1.000f),
                        overflow = Overflow.Hidden,
                    },
                    pickingMode = PickingMode.Ignore,
                });

                textColumn.Add(detailRow);

                row.Add(textColumn);
            }
            else
            {
                row.Add(new Label("Property Tweener")
                {
                    style =
                    {
                        fontSize = 10,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        color = new Color(0.945f, 0.945f, 0.945f, 1.000f),
                        overflow = Overflow.Hidden,
                    },
                    pickingMode = PickingMode.Ignore,
                });
            }

            block.Add(row);
        }

        private static string NicifiedPropertyNameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            int lastDot = path.LastIndexOf('.');
            string last = lastDot >= 0 ? path.Substring(lastDot + 1) : path;
            return ObjectNames.NicifyVariableName(last);
        }

        protected override int GetBlockColorSeed(Segment segment)
        {
            if (segment is not PropertyTweener tweener || !tweener.Property.IsValid || tweener.Property.Target is not Object obj)
                return base.GetBlockColorSeed(segment);

            return DeterministicHash($"{obj.GetType().FullName} ({tweener.Property.Path})");
        }
    }
}