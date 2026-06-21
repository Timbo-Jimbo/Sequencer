
namespace TimboJimbo.Sequencer.Builder
{
    public static class Seg
    {
        public static SegMake Make { get; } = new SegMake();
        public static SegSchedule Schedule { get; } = new SegSchedule();
    }
    public readonly struct SegMake { }
    public readonly struct SegSchedule { }
}