using System.Collections.Generic;
using TimboJimbo.PropertyBindings;

namespace TimboJimbo.Sequencer
{
    public enum SequenceValidationCode
    {
        EmptyName,
        DuplicateName,
        NullSequence,
        NullSegment,
        InvalidTiming,
        InvalidProperty,
        BindingResolutionFailed,
        MissingIncludedProvider,
        MissingIncludedSequence,
        RecursiveInclude,
        PlanConstructionFailed
    }

    public sealed class SequenceValidationIssue
    {
        public SequenceValidationCode Code { get; }
        public string Message { get; }
        public string SequenceName { get; }
        public Segment Segment { get; }
        public BindableProperty Property { get; }
        public BindingResolutionReport BindingResolution { get; }

        internal SequenceValidationIssue(
            SequenceValidationCode code,
            string message,
            string sequenceName = null,
            Segment segment = null,
            BindableProperty property = default,
            BindingResolutionReport bindingResolution = null)
        {
            Code = code;
            Message = message;
            SequenceName = sequenceName;
            Segment = segment;
            Property = property;
            BindingResolution = bindingResolution;
        }
    }

    public sealed class SequenceValidationReport
    {
        public IReadOnlyList<SequenceValidationIssue> Issues { get; }
        public bool IsValid => Issues.Count == 0;

        internal SequenceValidationReport(IReadOnlyList<SequenceValidationIssue> issues)
        {
            Issues = issues;
        }
    }
}