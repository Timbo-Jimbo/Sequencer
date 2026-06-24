using System;
using System.Collections.Generic;
using UnityEditor;

namespace TimboJimboEditor.Sequencer
{
    public static class EditorExtensionRegistry<TAttr, TBase> 
        where TAttr : Attribute 
        where TBase : class
    {
        private static Dictionary<Type, Type> _cachedTypes;
        private static readonly Func<TAttr, Type> _getInspectedType;

        static EditorExtensionRegistry()
        {
            // Dynamically fetch 'InspectedType' property from custom attributes
            var prop = typeof(TAttr).GetProperty("InspectedType", typeof(Type));
            if (prop != null)
            {
                _getInspectedType = attr => prop.GetValue(attr) as Type;
            }
        }

        private static void EnsureRegistry()
        {
            if (_cachedTypes != null)
                return;

            _cachedTypes = new Dictionary<Type, Type>();
            var foundTypes = TypeCache.GetTypesWithAttribute<TAttr>();
            foreach (var extType in foundTypes)
            {
                if (extType.IsAbstract || !typeof(TBase).IsAssignableFrom(extType))
                    continue;

                var attributes = (TAttr[])extType.GetCustomAttributes(typeof(TAttr), false);
                if (attributes.Length > 0 && _getInspectedType != null)
                {
                    var inspectedType = _getInspectedType(attributes[0]);
                    if (inspectedType != null)
                    {
                        _cachedTypes[inspectedType] = extType;
                    }
                }
            }
        }

        public static bool TryGetExtension(Type entityType, out TBase extension)
        {
            extension = null;
            if (entityType == null)
                return false;

            EnsureRegistry();
            if (_cachedTypes.TryGetValue(entityType, out var extType))
            {
                try
                {
                    extension = (TBase)Activator.CreateInstance(extType);
                    return extension != null;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"Failed to instantiate {typeof(TBase).Name} for {entityType.Name}: {e.Message}");
                }
            }
            return false;
        }

        public static IEnumerable<TBase> GetAllExtensions()
        {
            EnsureRegistry();
            var list = new List<TBase>();
            foreach (var extType in _cachedTypes.Values)
            {
                try
                {
                    var ext = (TBase)Activator.CreateInstance(extType);
                    if (ext != null)
                        list.Add(ext);
                }
                catch { }
            }
            return list;
        }
    }
}
