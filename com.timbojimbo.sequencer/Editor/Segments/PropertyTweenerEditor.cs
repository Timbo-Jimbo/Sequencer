using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimboJimboEditor.Sequencer.Segments
{
    [CustomSegmentEditor(typeof(PropertyTweener))]
    public sealed class PropertyTweenerEditor : SegmentEditor
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
                    marginLeft = 4,
                    marginTop = 2,
                    overflow = Overflow.Hidden,
                },
                pickingMode = PickingMode.Ignore,
            };

            if (tweener.Property.Target is Object obj)
            {
                var compName = ObjectNames.NicifyVariableName(obj.GetType().Name);
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

                textColumn.Add(new Label($"{(obj is Component c ? c.gameObject.name : obj.name)}")
                {
                    style =
                    {
                        fontSize = 10,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        color = new Color(0.941f, 0.941f, 0.941f, 1.000f),
                        overflow = Overflow.Hidden,
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

                var iconContent = EditorGUIUtility.ObjectContent(obj, obj.GetType());
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

                var propName = NicifiedPropertyNameFromPath(tweener.Property.Path);
                var detailsText = string.IsNullOrEmpty(propName) ? $"{compName} > (no property)" : $"{compName} > {propName}";
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
                row.Add(new Label("(no property)")
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
            if (segment is not PropertyTweener tweener || tweener.Property.Target is not Object obj)
                return base.GetBlockColorSeed(segment);

            return DeterministicHash($"{obj.GetType().FullName} ({tweener.Property.Path})");
        }

    }
}