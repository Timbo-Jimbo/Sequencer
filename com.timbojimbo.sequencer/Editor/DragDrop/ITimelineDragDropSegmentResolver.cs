using System.Collections.Generic;
using TimboJimbo.Sequencer;
using UnityEngine;

namespace TimboJimboEditor.Sequencer.DragDrop
{
    public readonly struct TimelineDragDropResolveContext
    {
        public readonly SequenceProvider TargetProvider;
        public readonly string TargetSequenceName;
        public readonly IReadOnlyList<GameObject> DraggedGameObjects;
        public readonly float TimelineTime;

        public TimelineDragDropResolveContext(
            SequenceProvider targetProvider,
            string targetSequenceName,
            IReadOnlyList<GameObject> draggedGameObjects,
            float timelineTime)
        {
            TargetProvider = targetProvider;
            TargetSequenceName = targetSequenceName;
            DraggedGameObjects = draggedGameObjects;
            TimelineTime = timelineTime;
        }
    }

    public readonly struct TimelineDragDropResolveResult
    {
        public readonly Segment Segment;

        public TimelineDragDropResolveResult(Segment segment)
        {
            Segment = segment;
        }
    }

    public interface ITimelineDragDropSegmentResolver
    {
        int Priority { get; }

        bool TryResolve(in TimelineDragDropResolveContext context, out TimelineDragDropResolveResult result);
    }
}
