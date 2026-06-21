using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

public static class AddableSegmentTypeRegistry
{
    private static List<(Type type, string menuName)> _addableSegmentTypes;

    public static IReadOnlyList<(Type type, string menuName)> AddableSegmentTypes
    {
        get
        {
            if (_addableSegmentTypes == null)
            {
                _addableSegmentTypes = new List<(Type type, string menuName)>();
                var foundTypes = TypeCache.GetTypesDerivedFrom<Segment>();
                
                foreach (var type in foundTypes)
                {
                    if (type.IsAbstract || type.IsGenericType)
                        continue;

                    if (!type.IsPublic && !type.IsNestedPublic)
                        continue;

                    if (type.GetConstructor(Type.EmptyTypes) == null)
                        continue;

                    var addSegmentMenuAttribute = type.GetCustomAttribute<AddSegmentMenuAttribute>();
                    var menuName = addSegmentMenuAttribute?.MenuName ?? ObjectNames.NicifyVariableName(type.Name);
                    
                    // Match AddCompnentMenu's behavior of ignoring empty or whitespace-only menu names
                    if (!string.IsNullOrWhiteSpace(menuName))
                        _addableSegmentTypes.Add((type, menuName));
                }

                _addableSegmentTypes.Sort((a, b) => string.CompareOrdinal(a.menuName, b.menuName));
            }

            return _addableSegmentTypes;
        }
    }
}
