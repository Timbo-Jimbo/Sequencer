
namespace TimboJimbo.Sequencer.Builder
{
    public static class Seq
    {
        public static SeqMake Make { get; } = new SeqMake();
        public static SeqSchedule Schedule { get; } = new SeqSchedule();
    }
    public readonly struct SeqMake { }
    public readonly struct SeqSchedule { }
}