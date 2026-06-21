using System;
using System.Collections.Generic;
using UnityEditor;

namespace TimboJimboEditor.Sequencer
{
    public static class SegmentRecorderRegistry
    {
        private static Dictionary<Type, Type> _recorderTypes;

        private static void EnsureRegistry()
        {
            if (_recorderTypes != null)
                return;

            _recorderTypes = new Dictionary<Type, Type>();
            var foundTypes = TypeCache.GetTypesWithAttribute<CustomSegmentRecorderAttribute>();
            foreach (var recorderType in foundTypes)
            {
                if (recorderType.IsAbstract || !typeof(SegmentRecorder).IsAssignableFrom(recorderType))
                    continue;

                var attributes = (CustomSegmentRecorderAttribute[])recorderType.GetCustomAttributes(typeof(CustomSegmentRecorderAttribute), false);
                if (attributes.Length > 0)
                {
                    var inspectedType = attributes[0].InspectedType;
                    _recorderTypes[inspectedType] = recorderType;
                }
            }
        }

        public static bool HasAnyRecorders()
        {
            EnsureRegistry();
            return _recorderTypes.Count > 0;
        }

        public static SegmentRecorder GetRecorderFor(Type segmentType)
        {
            EnsureRegistry();
            if (_recorderTypes.TryGetValue(segmentType, out var recorderType))
            {
                try
                {
                    return (SegmentRecorder)Activator.CreateInstance(recorderType);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"Failed to instantiate SegmentRecorder {recorderType.Name}: {e.Message}");
                }
            }
            return null;
        }

        public static IEnumerable<SegmentRecorder> GetAllRecorders()
        {
            EnsureRegistry();

            var list = new List<SegmentRecorder>();
            foreach (var recorderType in _recorderTypes.Values)
            {
                try
                {
                    var recorder = (SegmentRecorder)Activator.CreateInstance(recorderType);
                    if (recorder != null)
                        list.Add(recorder);
                }
                catch { }
            }

            list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return list;
        }
    }
}