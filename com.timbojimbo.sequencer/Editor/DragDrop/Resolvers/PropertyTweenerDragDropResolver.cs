using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimboEditor.Sequencer.DragDrop
{
    public sealed class PropertyTweenerDragDropResolver : ITimelineDragDropSegmentResolver
    {
        public int Priority => 10;

        public bool TryResolve(in TimelineDragDropResolveContext context, out TimelineDragDropResolveResult result)
        {
            if (context.DraggedGameObjects == null || context.DraggedGameObjects.Count == 0)
            {
                result = default;
                return false;
            }

            for (int i = 0; i < context.DraggedGameObjects.Count; i++)
            {
                var gameObject = context.DraggedGameObjects[i];
                if (gameObject == null)
                    continue;

                if(!gameObject.transform.IsChildOf(context.TargetProvider.transform))
                    continue;

                var segment = new PropertyTweener
                {
                    StartTime = Mathf.Max(0f, context.TimelineTime),
                    Duration = 1f,
                    Property = BindableProperty.CreateUnresolvedTarget(gameObject),
                };

                result = new TimelineDragDropResolveResult(segment);
                return true;
            }

            result = default;
            return false;
        }
    }
}
