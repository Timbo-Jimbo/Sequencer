using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;

namespace TimboJimboEditor.Sequencer
{
    public static class SegmentLocator
    {
        public static string FindPath(Sequence sequence, Segment target, string basePath = "Sequence")
        {
            if (sequence == null || target == null)
                return null;

            var list = sequence.Segments;
            if (list == null)
                return null;

            for (int i = 0; i < list.Count; i++)
            {
                var candidate = list[i];
                string path = $"{basePath}.Segments.Array.data[{i}]";
                if (ReferenceEquals(candidate, target))
                    return path;

                if (candidate is Sequence nested)
                {
                    var found = FindPath(nested, target, path);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }
    }
}
