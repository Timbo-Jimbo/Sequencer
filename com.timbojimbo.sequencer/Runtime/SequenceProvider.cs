using System;
using System.Collections.Generic;
using System.Linq;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimbo.Sequencer
{
    /// <summary>
    /// A serializable reference to a named <see cref="Sequence"/> on a <see cref="SequenceProvider"/>.
    /// This is the intended way for scripts to reference sequences: hold a <see cref="SequenceRef"/>
    /// field and call <see cref="Play"/> / <see cref="CreatePlayer"/> on it directly.
    /// </summary>
    [Serializable]
    public struct SequenceRef
    {
        public SequenceProvider Provider;
        public string SequenceName;

        /// <summary>True when the referenced provider exists and contains the named sequence.</summary>
        public bool IsValid => Provider != null && Provider.Contains(SequenceName);

        public bool TryResolve(out Sequence sequence)
        {
            if (Provider != null)
                return Provider.TryGetSequence(SequenceName, out sequence);

            sequence = null;
            return false;
        }

        public Sequence Resolve()
        {
            if (TryResolve(out var sequence))
                return sequence;

            throw new InvalidOperationException(
                $"ProviderSequence could not resolve a sequence (provider: '{(Provider != null ? Provider.name : "null")}', name: '{SequenceName}').");
        }

        /// <summary>Creates a player for the referenced sequence without starting it.</summary>
        public SequencePlayer CreatePlayer(bool isPreview = false, bool restoreValuesOnDispose = true, bool autoDrive = false)
        {
            return Resolve().CreatePlayer(isPreview, restoreValuesOnDispose, autoDrive);
        }

        /// <summary>Creates a self-driving player for the referenced sequence and starts it.</summary>
        public SequencePlayer Play(bool loop = false, float speed = 1f, bool restoreValuesOnDispose = true)
        {
            return Resolve().Play(loop, speed, restoreValuesOnDispose);
        }

        public static implicit operator Sequence(SequenceRef sequenceRef) => sequenceRef.Resolve();
    }

    public class SequenceProvider : MonoBehaviour
    {
        public List<Sequence> Sequences = new();

        /// <summary>
        /// Looks up a sequence by name. An empty/null name resolves to the first sequence when one exists.
        /// </summary>
        public bool TryGetSequence(string sequenceName, out Sequence sequence)
        {
            if (Sequences != null && Sequences.Count > 0)
            {
                if (string.IsNullOrEmpty(sequenceName))
                {
                    sequence = Sequences[0];
                    return sequence != null;
                }

                foreach (var candidate in Sequences)
                {
                    if (candidate != null && candidate.Name == sequenceName)
                    {
                        sequence = candidate;
                        return true;
                    }
                }
            }

            sequence = null;
            return false;
        }

        public bool Contains(string sequenceName) => TryGetSequence(sequenceName, out _);

        /// <summary>Returns an existing named sequence or creates one. Names must be non-empty and unique.</summary>
        public Sequence GetOrCreateSequence(string sequenceName)
        {
            if (string.IsNullOrWhiteSpace(sequenceName))
                throw new ArgumentException("Sequence name cannot be null, empty, or whitespace.", nameof(sequenceName));
            if (TryGetSequence(sequenceName, out var sequence))
                return sequence;

            sequence = new Sequence { Name = sequenceName };
            Sequences ??= new List<Sequence>();
            Sequences.Add(sequence);
            return sequence;
        }

        /// <summary>
        /// Creates or exactly replaces the contents of a named sequence. The provider owns its
        /// graph: supplied segments are deep-cloned, so callers keep no live reference into the
        /// provider and non-serializable state (delegates, code-added listeners) is not retained.
        /// </summary>
        public Sequence UpsertSequence(string sequenceName, IEnumerable<Segment> segments)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            var sequence = GetOrCreateSequence(sequenceName);
            sequence.ReplaceSegments(segments.Select(SegmentCloner.Clone));
            return sequence;
        }

        public bool RemoveSequence(string sequenceName)
        {
            if (Sequences == null) return false;
            for (int i = 0; i < Sequences.Count; i++)
            {
                if (Sequences[i] == null || Sequences[i].Name != sequenceName) continue;
                Sequences.RemoveAt(i);
                return true;
            }
            return false;
        }

        public Sequence GetSequence(string sequenceName)
        {
            if (TryGetSequence(sequenceName, out var sequence))
                return sequence;

            throw new Exception($"Sequence with name '{sequenceName}' not found.");
        }

        public bool TryGetPlan(string sequenceName, out SegmentPlan plan, SegmentPlan parent = null)
        {
            if (TryGetSequence(sequenceName, out var sequence))
            {
                plan = sequence.GetPlan(parent);
                return true;
            }

            plan = null;
            return false;
        }

        public SegmentPlan GetPlan(string sequenceName, SegmentPlan parent = null)
        {
            return GetSequence(sequenceName).GetPlan(parent);
        }

        /// <summary>Validates names, segment structure, timing, includes, and property binding resolution.</summary>
        public SequenceValidationReport ValidateSequences()
        {
            var issues = new List<SequenceValidationIssue>();
            var names = new HashSet<string>();

            if (Sequences == null)
                return new SequenceValidationReport(issues.AsReadOnly());

            for (int i = 0; i < Sequences.Count; i++)
            {
                var sequence = Sequences[i];
                if (sequence == null)
                {
                    issues.Add(new SequenceValidationIssue(SequenceValidationCode.NullSequence,
                        $"Sequence entry at index {i} is null."));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(sequence.Name))
                    issues.Add(new SequenceValidationIssue(SequenceValidationCode.EmptyName,
                        $"Sequence entry at index {i} has no name."));
                else if (!names.Add(sequence.Name))
                    issues.Add(new SequenceValidationIssue(SequenceValidationCode.DuplicateName,
                        $"Sequence name '{sequence.Name}' is duplicated.", sequence.Name));

                ValidateSequence(sequence, issues, new HashSet<(SequenceProvider, Sequence)>());
            }

            return new SequenceValidationReport(issues.AsReadOnly());
        }

        public SequenceValidationReport ValidateSequence(string sequenceName)
        {
            var issues = new List<SequenceValidationIssue>();
            if (!TryGetSequence(sequenceName, out var sequence))
            {
                issues.Add(new SequenceValidationIssue(SequenceValidationCode.MissingIncludedSequence,
                    $"Sequence '{sequenceName}' does not exist.", sequenceName));
                return new SequenceValidationReport(issues.AsReadOnly());
            }

            ValidateSequence(sequence, issues, new HashSet<(SequenceProvider, Sequence)>());
            return new SequenceValidationReport(issues.AsReadOnly());
        }

        private void ValidateSequence(
            Sequence sequence,
            List<SequenceValidationIssue> issues,
            HashSet<(SequenceProvider, Sequence)> includeStack)
        {
            var key = (this, sequence);
            if (!includeStack.Add(key))
            {
                issues.Add(new SequenceValidationIssue(SequenceValidationCode.RecursiveInclude,
                    $"Sequence '{sequence.Name}' is recursively included.", sequence.Name, sequence));
                return;
            }

            bool canBuildPlan = ValidateSegments(sequence.Segments, sequence.Name, issues, includeStack);
            if (canBuildPlan)
            {
                try
                {
                    ValidatePlan(sequence.GetPlan(null), sequence.Name, issues);
                }
                catch (Exception exception)
                {
                    issues.Add(new SequenceValidationIssue(SequenceValidationCode.PlanConstructionFailed,
                        $"Failed to construct plan for sequence '{sequence.Name}': {exception.Message}",
                        sequence.Name, sequence));
                }
            }

            includeStack.Remove(key);
        }

        private bool ValidateSegments(
            IReadOnlyList<Segment> segments,
            string sequenceName,
            List<SequenceValidationIssue> issues,
            HashSet<(SequenceProvider, Sequence)> includeStack)
        {
            if (segments == null)
            {
                issues.Add(new SequenceValidationIssue(SequenceValidationCode.NullSegment,
                    $"Sequence '{sequenceName}' has a null segment list.", sequenceName));
                return false;
            }

            bool validStructure = true;
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment == null)
                {
                    issues.Add(new SequenceValidationIssue(SequenceValidationCode.NullSegment,
                        $"Sequence '{sequenceName}' contains a null segment at index {i}.", sequenceName));
                    validStructure = false;
                    continue;
                }

                if (segment is Sequence nested)
                    validStructure &= ValidateSegments(nested.Segments, sequenceName, issues, includeStack);

                if (segment is PropertySegment propertySegment && !propertySegment.Property.IsValid)
                    issues.Add(new SequenceValidationIssue(SequenceValidationCode.InvalidProperty,
                        $"{segment.GetType().Name} in '{sequenceName}' has an invalid property.",
                        sequenceName, segment, propertySegment.Property));

                if (segment is InsertSequenceProvider include)
                {
                    if (include.Provider == null)
                    {
                        issues.Add(new SequenceValidationIssue(SequenceValidationCode.MissingIncludedProvider,
                            $"InsertSequenceProvider in '{sequenceName}' has no provider.", sequenceName, segment));
                    }
                    else if (!include.Provider.TryGetSequence(include.SequenceName, out var includedSequence))
                    {
                        issues.Add(new SequenceValidationIssue(SequenceValidationCode.MissingIncludedSequence,
                            $"Included sequence '{include.SequenceName}' was not found on provider '{include.Provider.name}'.",
                            sequenceName, segment));
                    }
                    else
                    {
                        include.Provider.ValidateSequence(includedSequence, issues, includeStack);
                    }
                }
            }
            return validStructure;
        }

        private void ValidatePlan(SegmentPlan rootPlan, string sequenceName, List<SequenceValidationIssue> issues)
        {
            var open = new Stack<SegmentPlan>();
            open.Push(rootPlan);
            while (open.Count > 0)
            {
                var plan = open.Pop();
                float start = plan.Timing.RelativeStartTime;
                float duration = plan.Timing.RelativeDuration;
                if (float.IsNaN(start) || float.IsInfinity(start) ||
                    float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
                {
                    issues.Add(new SequenceValidationIssue(SequenceValidationCode.InvalidTiming,
                        $"{plan.Segment?.GetType().Name ?? "Segment"} in '{sequenceName}' has invalid timing " +
                        $"(start={start}, duration={duration}).", sequenceName, plan.Segment));
                }

                foreach (var property in plan.Bindings.Properties)
                {
                    if (!property.IsValid) continue;
                    var resolution = PropertyBindingRegistry.Diagnose(gameObject, property);
                    if (!resolution.Success)
                        issues.Add(new SequenceValidationIssue(SequenceValidationCode.BindingResolutionFailed,
                            $"No binding can be constructed for '{property.Target.name}.{property.Path}' in '{sequenceName}'.",
                            sequenceName, plan.Segment, property, resolution));
                }

                for (int i = plan.Children.Count - 1; i >= 0; i--)
                    open.Push(plan.Children[i]);
            }
        }

        /// <summary>Creates a player for the named sequence without starting it.</summary>
        public SequencePlayer CreatePlayer(string sequenceName, bool isPreview = false, bool restoreValuesOnDispose = true, bool autoDrive = false)
        {
            return GetSequence(sequenceName).CreatePlayer(isPreview, restoreValuesOnDispose, autoDrive);
        }

        /// <summary>Creates a player for the named sequence over a playback range, without starting it.</summary>
        public SequencePlayer CreatePlayer(string sequenceName, PlaybackRange playbackRange, bool isPreview = false, bool restoreValuesOnDispose = true, bool autoDrive = false)
        {
            return GetSequence(sequenceName).CreatePlayer(playbackRange, isPreview, restoreValuesOnDispose, autoDrive);
        }

        /// <summary>Creates a self-driving player for the named sequence and starts it.</summary>
        public SequencePlayer Play(string sequenceName, bool loop = false, float speed = 1f, bool restoreValuesOnDispose = true)
        {
            return GetSequence(sequenceName).Play(loop, speed, restoreValuesOnDispose);
        }
    }
}