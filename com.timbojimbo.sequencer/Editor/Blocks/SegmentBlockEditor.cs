using System;
using System.Collections.Generic;
using TimboJimbo.Core.Utility;
using TimboJimbo.Sequencer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimboJimboEditor.Sequencer.Blocks
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CustomSegmentBlockEditorAttribute : EditorExtensionAttribute
    {

        public CustomSegmentBlockEditorAttribute(Type inspectedType) : base(inspectedType)
        {
        }
    }

    /// <summary>
    /// Custom block editor for a <see cref="Segment"/> type.
    /// </summary>
    public class SegmentBlockEditor
    {
        /// <summary>Populate the block overlay inside the timeline canvas.</summary>
        public virtual void OnBlockGUI(Segment segment, VisualElement block) => DefaultBlockGUI(segment, block);

        public virtual LanePacker.Group GetLanePackerGroup(Segment segment) => new ()
        {
            GroupId = DeterministicHash(segment.GetType().FullName),
            SubGroupId = GetBlockColorSeed(segment),
        };
        protected virtual int GetBlockColorSeed(Segment segment) => 0;

        /// <summary>Return custom fill/border colours, or <c>default</c> to use built-in fallback.</summary>
        public virtual (Color fill, Color border) GetBlockColors(Segment segment)
        {
            var (defaultFill, defaultBorder) = (new Color(0.30f, 0.20f, 0.14f), new Color(0.58f, 0.40f, 0.28f));

            var seed = GetBlockColorSeed(segment);
            return (
                ColorFromSeed(defaultFill, seed, false),
                ColorFromSeed(defaultBorder, seed, true)
            );

            static Color ColorFromSeed(Color referenceColor, int seed, bool isBorder)
            {
                if(seed == 0) return referenceColor;

                ColorExtra.RGBToOkLCh(referenceColor, out float l, out float c, out float h);
                h = Mathf.Repeat(seed / (float)int.MaxValue, 1f) * 360f;

                if (isBorder) l *= 1.025f;

                return ColorExtra.OkLChToRGB(l, c, h);
            }
        }
        protected static void DefaultBlockGUI(Segment segment, VisualElement block)
        {
            var nicifiedName = ObjectNames.NicifyVariableName(segment.GetType().Name);
            nicifiedName = nicifiedName.Replace(" Segment", "");
            var label = new Label(nicifiedName)
            {
                name = "Label",
                style =
                {
                    marginLeft = 4,
                    marginTop = 2,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = new Color(0.85f, 0.88f, 0.95f),
                    overflow = Overflow.Hidden,
                },
                pickingMode = PickingMode.Ignore,
            };
            block.Add(label);

            var nestedPreview = CreateNestedPreview(segment.GetPlan(null));
            block.Add(nestedPreview);
        }

        protected static VisualElement CreateNestedPreview(SegmentPlan plan, int currentDepth = 0, int maxDepth = 3)
        {
            var nestedPreviewContainer = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 1,
                    top = 1,
                    right = 1,
                    bottom = 1,
                },
                pickingMode = PickingMode.Ignore,
            };

            var children = plan.Children;
            if (children == null || children.Count == 0)
                return nestedPreviewContainer;

            float parentStart = plan.Timing.AbsoluteStartTime;
            float parentDuration = Mathf.Max(plan.Timing.AbsoluteDuration, 0.0001f);

            var editor = SegmentBlockEditorRegistry.GetEditorByType(plan.Segment.GetType());

            var packed = LanePacker.Pack(
                items: children,
                itemToInput: plan => new()
                {
                    Data = plan,
                    Group = editor.GetLanePackerGroup(plan.Segment),
                    Start = plan.Timing.AbsoluteStartTime,
                    End = plan.Timing.AbsoluteStartTime + plan.Timing.AbsoluteDuration,
                },
                depenetrateAndCompact: true
            );

            int laneCount = 1;
            for (int i = 0; i < packed.Count; i++)
                laneCount = Mathf.Max(laneCount, packed[i].Lane + 1);

            for (int i = 0; i < packed.Count; i++)
            {
                var packedEntry = packed[i];
                var widthAsPercent = packedEntry.Item.Timing.AbsoluteDuration / parentDuration;
                var leftAsPercent = (packedEntry.Item.Timing.AbsoluteStartTime - parentStart) / parentDuration;
                var topAsPercent = (float)packedEntry.Lane / laneCount;
                var heightAsPercent = 1f / laneCount;

                var segmentArea = new VisualElement
                {
                    style =
                    {
                        position = Position.Absolute,
                        left = Length.Percent(leftAsPercent * 100f),
                        top = Length.Percent(topAsPercent * 100f),
                        width = Length.Percent(widthAsPercent * 100f),
                        height = Length.Percent(heightAsPercent * 100f),
                    },
                    pickingMode = PickingMode.Ignore,
                    tooltip = ObjectNames.NicifyVariableName(packedEntry.Item.Segment.GetType().Name),
                };

                var baseOpacity = 0.5f;
                var baseAlpha = 0.3f;

                const float opacityMultPerDepth = 1.4f;
                const float alphaMultPerDepth = 1;

                var segmentRender = new VisualElement
                {
                    style =
                    {
                        position = Position.Absolute,
                        left = 1,
                        top = 1,
                        right = 1,
                        bottom = 1,
                        backgroundColor = new Color(1,1,1, baseAlpha * (Mathf.Pow(alphaMultPerDepth, currentDepth))),
                        opacity = baseOpacity * (Mathf.Pow(opacityMultPerDepth, currentDepth)),
                        borderTopLeftRadius = 3f + (currentDepth > 0 ? -1f : 0f),
                        borderTopRightRadius = 3f + (currentDepth > 0 ? -1f : 0f),
                        borderBottomLeftRadius = 3f + (currentDepth > 0 ? -1f : 0f),
                        borderBottomRightRadius = 3f + (currentDepth > 0 ? -1f : 0f),
                    },
                    pickingMode = PickingMode.Ignore,
                };
                segmentArea.Add(segmentRender);

                if (packedEntry.Item.Children != null && packedEntry.Item.Children.Count > 0 && currentDepth < maxDepth)
                {
                    var nested = CreateNestedPreview(packedEntry.Item, currentDepth + 1, maxDepth);
                    segmentRender.Add(nested);
                }

                nestedPreviewContainer.Add(segmentArea);
            }

            return nestedPreviewContainer;
        }

        protected static int DeterministicHash(string s)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in s)
                    hash = hash * 31 + c;
                return hash;
            }
        }
    }
}