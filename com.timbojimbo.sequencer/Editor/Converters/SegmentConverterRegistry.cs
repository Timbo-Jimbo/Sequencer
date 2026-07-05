using System;
using System.Collections.Generic;
using TimboJimbo.Sequencer;
using UnityEditor;

namespace TimboJimboEditor.Sequencer.Converters
{
    public static class SegmentConverterRegistry
    {
        private static List<SegmentConverter> _converters;

        private static void EnsureRegistry()
        {
            if (_converters != null)
                return;

            _converters = new List<SegmentConverter>();
            foreach (var type in TypeCache.GetTypesWithAttribute<SegmentConverterAttribute>())
            {
                if (type.IsAbstract || !typeof(SegmentConverter).IsAssignableFrom(type))
                    continue;

                try
                {
                    _converters.Add((SegmentConverter)Activator.CreateInstance(type));
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"Failed to instantiate SegmentConverter {type.Name}: {e.Message}");
                }
            }

            _converters.Sort((a, b) =>
            {
                int byPriority = b.Priority.CompareTo(a.Priority);
                return byPriority != 0 ? byPriority : string.Compare(a.MenuName, b.MenuName, StringComparison.Ordinal);
            });
        }

        public static List<SegmentConverter> GetConvertersFor(IReadOnlyList<Segment> segments)
        {
            var result = new List<SegmentConverter>();
            if (segments == null || segments.Count == 0)
                return result;

            EnsureRegistry();

            foreach (var converter in _converters)
            {
                try
                {
                    if (converter.CanConvert(segments))
                        result.Add(converter);
                }
                catch { }
            }

            return result;
        }
    }
}
