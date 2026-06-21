using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    public static class TimelineEditorUtility
    {
        public static List<ItemToLane<T>> PackIntoLanes<T>(
            IReadOnlyList<T> items,
            Func<T, float> itemStart,
            Func<T, float> itemEnd
        )
        {

            var sorted = new List<T>(items.Count);
            for (int i = 0; i < items.Count; i++)
                sorted.Add(items[i]);

            var minStart = float.MaxValue;
            var maxEnd = float.MinValue;
            
            sorted.Sort((a, b) =>
            {
                var startA = itemStart(a);
                var endA = itemEnd(a);

                var startB = itemStart(b);
                var endB = itemEnd(b);

                minStart = Mathf.Min(minStart, startA, startB);
                maxEnd = Mathf.Max(maxEnd, endA, endB);

                int byStart = startA.CompareTo(startB);
                if(byStart != 0) return byStart;
                var lengthA = endA - startA;
                var lengthB = endB - startB;
                return lengthB.CompareTo(lengthA);
            });

            var laneEnds = new List<float>();
            var packed = new List<ItemToLane<T>>(sorted.Count);
            var overlapThreshold = (maxEnd - minStart) * 0.0001f;


            for (int i = 0; i < sorted.Count; i++)
            {
                var item = sorted[i];
                float start = itemStart(item);
                float end = itemEnd(item);

                int lane = -1;
                for (int l = 0; l < laneEnds.Count; l++)
                {
                    if (start >= laneEnds[l] - overlapThreshold)
                    {
                        lane = l;
                        break;
                    }
                }

                if (lane < 0)
                {
                    lane = laneEnds.Count;
                    laneEnds.Add(float.MinValue);
                }

                laneEnds[lane] = end;
                packed.Add(new ItemToLane<T>(item, lane));
            }

            return packed;
        }

        public readonly struct ItemToLane<T>
        {
            public readonly T Item;
            public readonly int Lane;

            public ItemToLane(T item, int lane)
            {
                Item = item;
                Lane = lane;
            }
        }
    
    }
}