using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimboJimboEditor.Sequencer.Blocks
{
    [CustomSegmentBlockEditor(typeof(PropertyShaker))]
    public sealed class PropertyShakerBlockEditor : SegmentBlockEditor
    {
        public override void OnBlockGUI(Segment segment, VisualElement block)
        {
            if (segment is not PropertyShaker shaker)
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

            var targetGo = default(GameObject);
            if (shaker.Property.Target is Component component)
                targetGo = component.gameObject;
            else if (shaker.Property.Target is GameObject go)
                targetGo = go;

            if (targetGo != null)
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

                textColumn.Add(new Label(targetGo.name)
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Bold,
                        color = new Color(0.941f, 0.941f, 0.941f, 1f),
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

                var propName = NicifiedPropertyNameFromPath(shaker.Property.Path);
                if (string.IsNullOrWhiteSpace(propName))
                {
                    detailRow.Add(new Label("Select Property...")
                    {
                        style =
                        {
                            fontSize = 10,
                            color = new Color(0.820f, 0.820f, 0.820f, 1f),
                            overflow = Overflow.Hidden,
                        },
                        pickingMode = PickingMode.Ignore,
                    });
                }
                else
                {
                    var typeName = shaker.Property.Target.GetType().Name;
                    var iconContent = EditorGUIUtility.ObjectContent(shaker.Property.Target, shaker.Property.Target.GetType());

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

                    detailRow.Add(new Label($"{typeName} > {propName}")
                    {
                        style =
                        {
                            fontSize = 10,
                            color = new Color(0.820f, 0.820f, 0.820f, 1f),
                            overflow = Overflow.Hidden,
                        },
                        pickingMode = PickingMode.Ignore,
                    });
                }

                textColumn.Add(detailRow);
                row.Add(textColumn);
            }
            else
            {
                row.Add(new Label("Property Shaker")
                {
                    style =
                    {
                        fontSize = 10,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        color = new Color(0.945f, 0.945f, 0.945f, 1f),
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
            string last = lastDot >= 0 ? path[(lastDot + 1)..] : path;
            return ObjectNames.NicifyVariableName(last);
        }

        protected override int GetBlockColorSeed(Segment segment)
        {
            if (segment is not PropertyShaker shaker || !shaker.Property.IsValid || shaker.Property.Target is not Object obj)
                return base.GetBlockColorSeed(segment);

            return DeterministicHash($"{obj.GetType().FullName} ({shaker.Property.Path})");
        }
    }
}
