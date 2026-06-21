using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using TimboJimbo.Sequencer.Builder;
using UnityEngine;


namespace TimboJimbo.Sequencer.Segments
{
    [Serializable]
    public class FindAndInsertSequenceProviders : Segment, IStartTimeConfigurable
    {
        [Serializable]
        public struct SearchParams
        {
            public Transform SearchRoot;
            public SearchScope Scope;
            public Filters Filter;

            /// <summary>Optional name filter (regex). Empty = match all.</summary>
            public string NameRegex;
        }

        public enum SearchScope
        {
            RootOnly,
            DirectChildrenOnly,
            EntireHierarchy,
        }

        [Flags]
        public enum Filters
        {
            None = 0,
            ExcludeInactiveInHierarchy = 1 << 0,
            ExcludeInactiveSelf = 1 << 1,
        }

        public enum ChildSort
        {
            ByDiscoveryOrder,
            ByXPosition,
            ByYPosition
        }

        public List<SearchParams> InclusionSearches = new List<SearchParams>();
        public List<SearchParams> ExclusionSearches = new List<SearchParams>();

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

                var includedProviders = new List<SequenceProvider>();
                foreach (var search in InclusionSearches)
                    Collect(search, includedProviders);


                var excludedProviders = new List<SequenceProvider>();
                foreach (var search in ExclusionSearches)
                    Collect(search, excludedProviders);

                for (int i = includedProviders.Count - 1; i >= 0; i--)
                {
                    var excluded = excludedProviders.Contains(includedProviders[i]);
                    if (excluded)
                        includedProviders.RemoveAt(i);
                }

                for (int i = 0; i < includedProviders.Count; i++)
                {
                    var provider = includedProviders[i];
                    var childPlan = provider.GetPlan(plan);
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

        private void Collect(in SearchParams search, List<SequenceProvider> results)
        {
            if (search.SearchRoot == null)
                return;

            Regex regex = null;
            if (!string.IsNullOrEmpty(search.NameRegex))
            {
                try
                {
                    regex = new Regex(search.NameRegex);
                }
                catch
                {
                    return;
                }
            }

            if (search.Scope == SearchScope.RootOnly)
            {
                if ((search.Filter & Filters.ExcludeInactiveInHierarchy) != 0 && !search.SearchRoot.gameObject.activeInHierarchy)
                    return;
                if ((search.Filter & Filters.ExcludeInactiveSelf) != 0 && !search.SearchRoot.gameObject.activeSelf)
                    return;
                if (regex != null && !regex.IsMatch(search.SearchRoot.name))
                    return;
                if (
                    search.SearchRoot.TryGetComponent<SequenceProvider>(out var provider) 
                    && provider.Sequence != null 
                    && !results.Contains(provider)
                )
                {
                    results.Add(provider);
                }
            }
            else if (search.Scope == SearchScope.DirectChildrenOnly)
            {
                foreach (Transform child in search.SearchRoot)
                {
                    if ((search.Filter & Filters.ExcludeInactiveInHierarchy) != 0 && !child.gameObject.activeInHierarchy)
                        continue;
                    if ((search.Filter & Filters.ExcludeInactiveSelf) != 0 && !child.gameObject.activeSelf)
                        continue;
                    if (regex != null && !regex.IsMatch(child.name))
                        continue;
                    if (child.TryGetComponent<SequenceProvider>(out var provider)
                        && provider.Sequence != null && !results.Contains(provider))
                        results.Add(provider);
                }
            }
            else
            {
                var providers = search.SearchRoot.GetComponentsInChildren<SequenceProvider>(
                    includeInactive: (search.Filter & Filters.ExcludeInactiveInHierarchy) == 0
                );

                foreach (var provider in providers)
                {
                    if ((search.Filter & Filters.ExcludeInactiveSelf) != 0 && !provider.gameObject.activeSelf)
                        continue;
                    
                    if (regex != null && !regex.IsMatch(provider.name))
                        continue;
                    if (provider.Sequence != null && !results.Contains(provider))
                        results.Add(provider);
                }
            }

            if(results.Count > 1)
            {
                switch (Sorting)
                {
                    case ChildSort.ByXPosition:
                        results.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
                        break;
                    case ChildSort.ByYPosition:
                        results.Sort((a, b) => a.transform.position.y.CompareTo(b.transform.position.y));
                        break;
                }
            }
        }
    }

    public static class FindAndInsertSequenceProvidersExtensions
    {
        public static Segment FindAndPlayDirectChildren(
            this SegMake _,
            Transform search,
            float staggerBy = 0f,
            FindAndInsertSequenceProviders.ChildSort sort = FindAndInsertSequenceProviders.ChildSort.ByDiscoveryOrder
        )
        {
            return new FindAndInsertSequenceProviders
            {
                StaggerDelay = staggerBy,
                Sorting = sort,
                InclusionSearches =
                {
                    new FindAndInsertSequenceProviders.SearchParams
                    {
                        SearchRoot = search,
                        Scope = FindAndInsertSequenceProviders.SearchScope.DirectChildrenOnly,
                    }
                }
            };
        }
    }
}