using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Pool;

namespace TimboJimboEditor.Sequencer
{

    public static class LanePacker
    {
        
        private static bool TryGetOverlapInLane<T>(IReadOnlyList<LanePackingEntryState<T>> items, LanePackingEntryState<T> testTarget, int laneIndex, out LanePackingEntryState<T> overlap)
        {
            foreach (var existing in items)
            {
                if(existing.LaneIndex != laneIndex)
                    continue;

                if (existing.Data.Equals(testTarget.Data))
                    continue;

                if (existing.Start < testTarget.End &&
                    existing.End > testTarget.Start)
                {
                    overlap = existing;
                    return true;
                }
            }

            overlap = default;
            return false;
        }

        public static List<ItemLanePair<T>> Pack<T>(
            IReadOnlyList<T> items,
            Func<T, PackInput<T>> itemToInput,
            bool depenetrateAndCompact
        )
        {
            using(ListPool<LanePackingEntryState<T>>.Get(out var packStates))
            {
                foreach (var item in items)
                {
                    var packInput = itemToInput(item);
                    packStates.Add(new LanePackingEntryState<T>()
                    {
                        Data = packInput.Data,
                        Group = packInput.Group,
                        Start = Mathf.RoundToInt(packInput.Start * 100) / 100f,
                        End = Mathf.RoundToInt(packInput.End * 100) / 100f,
                        LaneIndex = 0,
                    });
                }

                packStates.Sort((a, b) =>
                {
                    int byGroup = a.Group.GroupId.CompareTo(b.Group.GroupId);
                    if (byGroup != 0) return byGroup;

                    int bySubGroup = a.Group.SubGroupId.CompareTo(b.Group.SubGroupId);
                    if (bySubGroup != 0) return bySubGroup;

                    int byStart = a.Start.CompareTo(b.Start);
                    if (byStart != 0) return byStart;

                    return a.End.CompareTo(b.End);
                });

                // Assign initial lanes
                int lastLaneIndex = -1;
                {
                    int lastGroupId = int.MinValue;
                    int lastSubGroupId = int.MinValue;

                    for (int i = 0; i < packStates.Count; i++)
                    {
                        LanePackingEntryState<T> entry = packStates[i];

                        if (entry.Group.GroupId != lastGroupId)
                        {
                            lastLaneIndex++;
                            lastGroupId = entry.Group.GroupId;
                            lastSubGroupId = entry.Group.SubGroupId;
                        }

                        if (entry.Group.SubGroupId != lastSubGroupId)
                        {
                            lastLaneIndex++;
                            lastSubGroupId = entry.Group.SubGroupId;
                        }

                        entry.LaneIndex = lastLaneIndex;
                        
                        packStates[i] = entry;
                    }
                }

                packStates.Sort((a, b) =>
                {
                    int byStart = a.Start.CompareTo(b.Start);
                    if (byStart != 0) return byStart;

                    int byDuration = (a.End - a.Start).CompareTo(b.End - b.Start);
                    return byDuration;
                });

                // Depenetrate and compact lanes
                if(depenetrateAndCompact)
                {
                    //depenetrate
                    {
                        for(int laneIndex = 0; laneIndex <= lastLaneIndex; laneIndex++)
                        {
                            bool wantsToRecheckLane = false;

                            for (int entryIndex = 0; entryIndex < packStates.Count; entryIndex++)
                            {
                                LanePackingEntryState<T> entry = packStates[entryIndex];
                                try
                                {
                                    if (entry.LaneIndex != laneIndex)
                                        continue;

                                    //move to next lane if overlaps
                                    if(TryGetOverlapInLane(packStates, entry, entry.LaneIndex, out var other))
                                    {
                                        // the one with lower move count should move. if tie, later one!
                                        if (entry.MoveCount < other.MoveCount || (entry.MoveCount == other.MoveCount && entry.Start >= other.Start))
                                        {
                                            entry.LaneIndex++;
                                            entry.MoveCount++;
                                            lastLaneIndex = Mathf.Max(lastLaneIndex, entry.LaneIndex);
                                            wantsToRecheckLane = true;
                                            continue;
                                        }
                                    }
                                }
                                finally
                                {
                                    packStates[entryIndex] = entry;
                                }
                            }

                            if (wantsToRecheckLane)
                            {
                                laneIndex--;
                                continue;
                            }
                        }
                    }

                    //compaction
                    {
                        bool wantsToScanAgain = true;

                        for (int entryIndex = 0; entryIndex < packStates.Count; entryIndex++)
                        {
                            LanePackingEntryState<T> entry = packStates[entryIndex];
                            try
                            {
                                if (entry.LaneIndex > 0)
                                {
                                    int targetLane = entry.LaneIndex - 1;
                                    if (!TryGetOverlapInLane(packStates, entry, targetLane, out var _))
                                    {
                                        entry.LaneIndex--;
                                        entry.MoveCount++;
                                        wantsToScanAgain = true;
                                    }
                                }
                            }
                            finally
                            {
                                packStates[entryIndex] = entry;
                            }

                            if (wantsToScanAgain)
                            {
                                entryIndex = -1;
                                wantsToScanAgain = false;
                            }
                        }
                    }
                }

                return packStates.Select(entry => new ItemLanePair<T>(
                    entry.Data,
                    entry.LaneIndex
                )).ToList();
            }
        }

        public readonly struct ItemLanePair<T>
        {
            public readonly T Item;
            public readonly int Lane;

            public ItemLanePair(T item, int lane)
            {
                Item = item;
                Lane = lane;
            }
        }
    
        private struct LanePackingEntryState<T>
        {
            public Group Group;
            public T Data;
            public int LaneIndex;
            public int MoveCount;
            public float Start;
            public float End;
        }

        public struct Group
        {
            public int GroupId;
            public int SubGroupId;
        }

        public struct PackInput<T>
        {
            public T Data;
            public Group Group;
            public float Start;
            public float End;
        }

    }
}