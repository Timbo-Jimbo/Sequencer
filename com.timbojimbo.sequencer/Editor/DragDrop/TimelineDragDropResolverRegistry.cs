using System;
using System.Collections.Generic;
using UnityEditor;

namespace TimboJimboEditor.Sequencer.DragDrop
{
    public static class TimelineDragDropResolverRegistry
    {
        private static List<ITimelineDragDropSegmentResolver> _resolvers;

        private static void EnsureResolvers()
        {
            if (_resolvers != null)
                return;

            _resolvers = new List<ITimelineDragDropSegmentResolver>();
            var resolverTypes = TypeCache.GetTypesDerivedFrom<ITimelineDragDropSegmentResolver>();
            for (int i = 0; i < resolverTypes.Count; i++)
            {
                var resolverType = resolverTypes[i];
                if (resolverType == null || resolverType.IsAbstract || resolverType.IsInterface)
                    continue;

                try
                {
                    if (Activator.CreateInstance(resolverType) is ITimelineDragDropSegmentResolver resolver)
                        _resolvers.Add(resolver);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"Failed to instantiate drag/drop resolver {resolverType.Name}: {e.Message}");
                }
            }

            _resolvers.Sort((a, b) =>
            {
                int byPriority = b.Priority.CompareTo(a.Priority);
                if (byPriority != 0)
                    return byPriority;

                return string.Compare(a.GetType().FullName, b.GetType().FullName, StringComparison.Ordinal);
            });
        }

        public static bool TryResolve(in TimelineDragDropResolveContext context, out TimelineDragDropResolveResult result)
        {
            EnsureResolvers();

            for (int i = 0; i < _resolvers.Count; i++)
            {
                if (_resolvers[i].TryResolve(context, out result))
                    return true;
            }

            result = default;
            return false;
        }
    }
}
