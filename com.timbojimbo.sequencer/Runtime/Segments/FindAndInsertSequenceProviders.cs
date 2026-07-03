using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using TimboJimbo.Sequencer.Builder;
using UnityEngine;
using UnityEngine.Pool;


namespace TimboJimbo.Sequencer.Segments
{
    [Serializable]
    public struct SequenceSearchParams
    {
        public Transform SearchRoot;
        public bool LimitDepth;
        public int Depth;
        public bool ExcludeInactiveInHierarchy;
        public bool ExcludeInactiveSelf;
        public bool FilterByProviderName;
        public string ProviderNameRegex;
        public bool FilterBySequenceName;
        public string SequenceNameRegex;

        public bool Valid => SearchRoot != null;
    }

    [Serializable]
    public class FindAndInsertSequenceProviders : Segment, IStartTimeConfigurable
    {
        public enum ChildSort
        {
            ByDiscoveryOrder,
            ByXPosition,
            ByYPosition
        }

        public List<SequenceSearchParams> InclusionSearches = new List<SequenceSearchParams>();
        public List<SequenceSearchParams> ExclusionSearches = new List<SequenceSearchParams>();

        public float StaggerDelay;
        public ChildSort Sorting;
        public float StartTime;

        // Recursion guard
        private bool _isBuildingPlan;

        public void SetStartTime(float startTime) => StartTime = startTime;
        public float GetStartTime() => StartTime;

        public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
        {
            if(_isBuildingPlan)
            {
                Debug.LogWarning("Detected recursive inclusion in FindAndInsertSequenceProviders. This is not supported and will likely lead to unexpected behaviour.");
                return new SegmentPlan(this, parent)
                {
                    Timing = { RelativeStartTime = StartTime, RelativeDuration = 0f }
                };
            }

            try
            {
                _isBuildingPlan = true;
                
                var plan = new SegmentPlan(this, parent)
                {
                    Timing = { RelativeStartTime = StartTime }
                };

                var includedProviders = new List<(SequenceProvider sequenceProvider, string sequenceName)>();
                foreach (var search in InclusionSearches)
                    Collect(search, includedProviders);


                var excludedProviders = new List<(SequenceProvider sequenceProvider, string sequenceName)>();
                foreach (var search in ExclusionSearches)
                    Collect(search, excludedProviders);

                for (int i = includedProviders.Count - 1; i >= 0; i--)
                {
                    var excluded = excludedProviders.Contains(includedProviders[i]);
                    if (excluded) includedProviders.RemoveAt(i);
                }

                for (int i = 0; i < includedProviders.Count; i++)
                {
                    var provider = includedProviders[i];
                    var childPlan = provider.sequenceProvider.GetPlan(provider.sequenceName, plan);
                    childPlan.Timing.RelativeStartTime += i * Mathf.Max(StaggerDelay, 0f);
                    var endsAt = childPlan.Timing.RelativeEndTime;

                    if (endsAt > plan.Timing.RelativeDuration)
                        plan.Timing.RelativeDuration = endsAt;
                }


                return plan;
            }
            finally
            {
                _isBuildingPlan = false;
            }

        }

        private void Collect(in SequenceSearchParams search, List<(SequenceProvider sequenceProvider, string sequenceName)> results)
        {
            if (!search.Valid)
                return;

            Regex providerNameRegex = null;
            if (search.FilterByProviderName && !string.IsNullOrEmpty(search.ProviderNameRegex))
            {
                try
                {
                    providerNameRegex = new Regex(search.ProviderNameRegex);
                }
                catch
                {
                    return;
                }
            }

            Regex sequenceNameRegex = null;
            if (search.FilterBySequenceName && !string.IsNullOrEmpty(search.SequenceNameRegex))
            {
                try
                {
                    sequenceNameRegex = new Regex(search.SequenceNameRegex);
                }
                catch
                {
                    return;
                }
            }

            using(ListPool<(Transform target, int depth)>.Get(out var openList))
            {
                openList.Add((search.SearchRoot, 0));

                while (openList.Count > 0)
                {
                    var (current, currentDepth) = openList[openList.Count - 1];
                    openList.RemoveAt(openList.Count - 1);

                    if (current == null)
                        continue;

                    if ((search.ExcludeInactiveInHierarchy && !current.gameObject.activeInHierarchy) || (search.ExcludeInactiveSelf && !current.gameObject.activeSelf))
                        continue;

                    if (providerNameRegex != null && !providerNameRegex.IsMatch(current.name))
                        continue;

                    if (current.TryGetComponent<SequenceProvider>(out var provider))
                    {
                        foreach(var sequence in provider.Sequences)
                        {
                            if (sequenceNameRegex != null && !sequenceNameRegex.IsMatch(sequence.Name))
                                continue;

                            results.Add((provider, sequence.Name));
                        }
                    }

                    if (!search.LimitDepth || currentDepth < search.Depth)
                    {
                        foreach (Transform child in current)
                        {
                            openList.Add((child, currentDepth + 1));
                        }
                    }
                }
            }
            

            if(results.Count > 1)
            {
                switch (Sorting)
                {
                    case ChildSort.ByXPosition:
                        results.Sort((a, b) => a.sequenceProvider.transform.position.x.CompareTo(b.sequenceProvider.transform.position.x));
                        break;
                    case ChildSort.ByYPosition:
                        results.Sort((a, b) => a.sequenceProvider.transform.position.y.CompareTo(b.sequenceProvider.transform.position.y));
                        break;
                }
            }
        }
    }

    public static class FindAndInsertSequenceProvidersExtensions
    {
        public static Segment FindAndPlay(
            this SeqMake _,
            SequenceSearchParams searchFor,
            float staggerBy = 0f,
            SequenceSearchParams exclude = default,
            FindAndInsertSequenceProviders.ChildSort sort = FindAndInsertSequenceProviders.ChildSort.ByDiscoveryOrder
        )
        {
            return new FindAndInsertSequenceProviders
            {
                StaggerDelay = staggerBy,
                Sorting = sort,
                InclusionSearches =
                {
                    searchFor
                },
                ExclusionSearches =
                {
                    exclude
                },
            };
        }
    }
}