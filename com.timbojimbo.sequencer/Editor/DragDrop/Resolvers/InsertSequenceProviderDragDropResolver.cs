using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimboEditor.Sequencer.DragDrop
{
    public sealed class InsertSequenceProviderDragDropResolver : ITimelineDragDropSegmentResolver
    {
        public int Priority => 100;

        public bool TryResolve(in TimelineDragDropResolveContext context, out TimelineDragDropResolveResult result)
        {
            if (context.DraggedGameObjects == null || context.DraggedGameObjects.Count == 0)
            {
                result = default;
                return false;
            }

            for (int i = 0; i < context.DraggedGameObjects.Count; i++)
            {
                if (!TryGetDraggedSequenceProvider(context.DraggedGameObjects[i], out var provider) || provider == null)
                    continue;

                var sequenceName = TimelineSessionState.ResolveValidSequenceName(provider, null);
                if (string.IsNullOrWhiteSpace(sequenceName))
                    continue;

                var segment = new InsertSequenceProvider
                {
                    Provider = provider,
                    SequenceName = sequenceName,
                };
                segment.SetStartTime(Mathf.Max(0f, context.TimelineTime));

                result = new TimelineDragDropResolveResult(segment);
                return true;
            }

            result = default;
            return false;
        }

        private static bool TryGetDraggedSequenceProvider(GameObject draggedObject, out SequenceProvider provider)
        {
            provider = null;
            if (draggedObject == null)
                return false;

            provider = draggedObject.GetComponent<SequenceProvider>();
            return provider != null;
        }
    }
}
