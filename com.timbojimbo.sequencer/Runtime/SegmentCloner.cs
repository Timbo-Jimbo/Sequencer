using UnityEngine;

namespace TimboJimbo.Sequencer
{
    /// <summary>
    /// Single cloning contract for authored segment graphs. Supports exactly what Unity's
    /// JSON serialization supports: [SerializeReference] polymorphism, nested sequences and
    /// Unity object references. Runtime-only state (delegates, UnityEvent listeners added
    /// in code) is not preserved.
    /// </summary>
    internal static class SegmentCloner
    {
        public static Segment Clone(Segment source)
        {
            if (source == null)
                return null;

            return JsonUtility.FromJson(JsonUtility.ToJson(source), source.GetType()) as Segment;
        }

        public static T Clone<T>(T source) where T : Segment => Clone((Segment)source) as T;

        /// <summary>Copies serialized state from <paramref name="source"/> into an existing instance of the same type.</summary>
        public static bool TryCopyInto(Segment source, Segment destination)
        {
            if (source == null || destination == null || source.GetType() != destination.GetType())
                return false;

            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), destination);
            return true;
        }
    }
}
