using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Builder;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TimboJimboTests.Sequencer
{
    public sealed class SequenceValidationTests
    {
        private readonly List<GameObject> _gameObjects = new();
        private SequenceProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _provider = CreateProvider("Validation provider");
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _gameObjects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_gameObjects[i]);
        }

        [Test]
        public void ValidateSequences_ReportsNullEmptyAndDuplicateEntriesDeterministically()
        {
            _provider.Sequences.Add(null);
            _provider.Sequences.Add(new Sequence { Name = " " });
            _provider.Sequences.Add(new Sequence { Name = "Duplicate" });
            _provider.Sequences.Add(new Sequence { Name = "Duplicate" });

            var report = _provider.ValidateSequences();

            Assert.That(report.Issues.Select(issue => issue.Code), Is.EqualTo(new[]
            {
                SequenceValidationCode.NullSequence,
                SequenceValidationCode.EmptyName,
                SequenceValidationCode.DuplicateName
            }));
            Assert.That(report.Issues[2].SequenceName, Is.EqualTo("Duplicate"));
        }

        [Test]
        public void ValidateSequence_ReportsNullSegmentAndDoesNotAttemptPlanConstruction()
        {
            var sequence = _provider.GetOrCreateSequence("Null segment");
            sequence.Segments.Add(null);
            sequence.Segments.Add(new ThrowingPlanSegment());

            var report = _provider.ValidateSequence(sequence.Name);

            Assert.That(report.Issues.Count(issue => issue.Code == SequenceValidationCode.NullSegment), Is.EqualTo(1));
            Assert.That(report.Issues.Any(issue => issue.Code == SequenceValidationCode.PlanConstructionFailed), Is.False);
        }

        [TestCase(float.NaN, 1f)]
        [TestCase(float.PositiveInfinity, 1f)]
        [TestCase(0f, -1f)]
        [TestCase(0f, float.NegativeInfinity)]
        public void ValidateSequence_ReportsInvalidTiming(float start, float duration)
        {
            var segment = new ValidationTimingSegment { StartTime = start, Duration = duration };
            var owned = _provider.UpsertSequence("Invalid timing", new[] { segment }).Segments[0];

            var report = _provider.ValidateSequence("Invalid timing");
            var timingIssues = report.Issues
                .Where(issue => issue.Code == SequenceValidationCode.InvalidTiming)
                .ToArray();
            var issue = timingIssues.Single(candidate => candidate.Segment == owned);

            Assert.That(issue.Segment, Is.SameAs(owned));
            Assert.That(issue.SequenceName, Is.EqualTo("Invalid timing"));
            Assert.That(timingIssues.All(candidate => candidate.Segment != null), Is.True);
        }

        [Test]
        public void ValidateSequence_ReportsInvalidProperty()
        {
            var setter = new PropertySetter { Property = BindableProperty.Invalid };
            var owned = _provider.UpsertSequence("Invalid property", new Segment[] { setter }).Segments[0];

            var issue = SingleIssue(_provider.ValidateSequence("Invalid property"), SequenceValidationCode.InvalidProperty);

            Assert.That(issue.Segment, Is.SameAs(owned));
            Assert.That(issue.Property.IsValid, Is.False);
        }

        [Test]
        public void BindingFailure_PreservesResolutionDetailsWithoutMutatingTarget()
        {
            var initial = new Vector3(2f, 3f, 4f);
            _provider.transform.localPosition = initial;
            var property = BindableProperty.CreateAdHoc(
                _provider.transform,
                "__MissingValidationProperty__",
                ValueKind.Float);
            var setter = new PropertySetter
            {
                Property = property,
                Value = ValueContainer.FromFloat(1f)
            };
            _provider.UpsertSequence("Binding failure", new Segment[] { setter });

            var issue = SingleIssue(_provider.ValidateSequence("Binding failure"), SequenceValidationCode.BindingResolutionFailed);

            Assert.That(issue.Property, Is.EqualTo(property));
            Assert.That(issue.BindingResolution, Is.Not.Null);
            Assert.That(issue.BindingResolution.Success, Is.False);
            Assert.That(issue.BindingResolution.Property, Is.EqualTo(property));
            Assert.That(issue.BindingResolution.Candidates, Is.Not.Empty);
            Assert.That(_provider.transform.localPosition, Is.EqualTo(initial));
        }

        [Test]
        public void ValidateSequence_ReportsMissingIncludedProvider()
        {
            var include = new InsertSequenceProvider { SequenceName = "Missing" };
            var owned = _provider.UpsertSequence("Root", new Segment[] { include }).Segments[0];

            var issue = SingleIssue(_provider.ValidateSequence("Root"), SequenceValidationCode.MissingIncludedProvider);

            Assert.That(issue.Segment, Is.SameAs(owned));
            Assert.That(issue.SequenceName, Is.EqualTo("Root"));
        }

        [Test]
        public void ValidateSequence_ReportsMissingIncludedSequence()
        {
            var includedProvider = CreateProvider("Included provider");
            var include = new InsertSequenceProvider
            {
                Provider = includedProvider,
                SequenceName = "Missing"
            };
            var owned = _provider.UpsertSequence("Root", new Segment[] { include }).Segments[0];

            var issue = SingleIssue(_provider.ValidateSequence("Root"), SequenceValidationCode.MissingIncludedSequence);

            Assert.That(issue.Segment, Is.SameAs(owned));
            StringAssert.Contains("Missing", issue.Message);
        }

        [Test]
        public void RecursiveInclude_TerminatesAndReportsRelevantSequence()
        {
            var include = new InsertSequenceProvider
            {
                Provider = _provider,
                SequenceName = "Recursive"
            };
            _provider.UpsertSequence("Recursive", new Segment[] { include });
            LogAssert.Expect(LogType.Warning, new Regex("Detected recursive inclusion"));

            var issue = SingleIssue(_provider.ValidateSequence("Recursive"), SequenceValidationCode.RecursiveInclude);

            Assert.That(issue.SequenceName, Is.EqualTo("Recursive"));
            Assert.That(issue.Segment, Is.SameAs(_provider.GetSequence("Recursive")));
        }

        [Test]
        public void PlanConstructionFailure_PreservesExceptionContext()
        {
            _provider.UpsertSequence("Broken plan", new Segment[] { new ThrowingPlanSegment() });

            var issue = SingleIssue(_provider.ValidateSequence("Broken plan"), SequenceValidationCode.PlanConstructionFailed);

            StringAssert.Contains(ThrowingPlanSegment.FailureMessage, issue.Message);
            Assert.That(issue.SequenceName, Is.EqualTo("Broken plan"));
        }

        [Test]
        public void Validation_DoesNotMutateProviderGraphOrSceneTarget()
        {
            var target = CreateGameObject("Validation target");
            var initial = new Vector3(3f, 4f, 5f);
            target.transform.localPosition = initial;
            var setter = Seq.Make.SetPosition(target.transform, Vector3.one * 9f);
            var sequence = _provider.UpsertSequence("Valid", new Segment[] { setter });
            var originalSequences = _provider.Sequences.ToArray();
            var originalSegments = sequence.Segments.ToArray();

            var report = _provider.ValidateSequences();

            Assert.That(report.IsValid, Is.True);
            Assert.That(_provider.Sequences, Is.EqualTo(originalSequences));
            Assert.That(sequence.Segments, Is.EqualTo(originalSegments));
            Assert.That(target.transform.localPosition, Is.EqualTo(initial));
        }

        [Test]
        public void ValidationAndCompilation_AgreeForSafeInvalidCases()
        {
            var invalidProperty = new PropertySetter
            {
                Property = BindableProperty.Invalid,
                StartTime = 0.5f
            };
            var missingInclude = new InsertSequenceProvider { StartTime = 0.5f };
            var sequence = _provider.UpsertSequence("Safe invalid", new Segment[] { invalidProperty, missingInclude });

            var report = _provider.ValidateSequence(sequence.Name);

            Assert.That(report.Issues.Select(issue => issue.Code), Does.Contain(SequenceValidationCode.InvalidProperty));
            Assert.That(report.Issues.Select(issue => issue.Code), Does.Contain(SequenceValidationCode.MissingIncludedProvider));
            using var player = sequence.CreatePlayer();
            player.Play();
            Assert.DoesNotThrow(() => player.Tick(0.5f));
        }

        private SequenceProvider CreateProvider(string name)
        {
            return CreateGameObject(name).AddComponent<SequenceProvider>();
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            _gameObjects.Add(gameObject);
            return gameObject;
        }

        private static SequenceValidationIssue SingleIssue(SequenceValidationReport report, SequenceValidationCode code)
        {
            return report.Issues.Single(issue => issue.Code == code);
        }
    }

    internal sealed class ValidationTimingSegment : Segment
    {
        public float StartTime;
        public float Duration;

        public override SegmentPlan GetPlan(SegmentPlan parent)
        {
            return new SegmentPlan(this, parent)
            {
                Timing = { RelativeStartTime = StartTime, RelativeDuration = Duration }
            };
        }
    }
}