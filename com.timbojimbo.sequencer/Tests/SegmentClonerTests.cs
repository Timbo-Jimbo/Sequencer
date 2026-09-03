using NUnit.Framework;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Builder;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimboJimboTests.Sequencer
{
    public sealed class SegmentClonerTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp() => _target = new GameObject("Clone target");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_target);

        [Test]
        public void Clone_DeepCopiesNestedPolymorphicGraphAndKeepsObjectReferences()
        {
            var setter = Seq.Make.SetPosition(_target.transform, Vector3.one);
            setter.StartTime = 0.5f;
            var inner = new Sequence { Name = "Inner" };
            inner.Add(setter);
            var outer = new Sequence { Name = "Outer" };
            outer.Add(inner);

            var clone = SegmentCloner.Clone(outer);

            Assert.That(clone, Is.Not.SameAs(outer));
            var clonedInner = (Sequence)clone.Segments[0];
            var clonedSetter = (PropertySetter)clonedInner.Segments[0];
            Assert.That(clonedInner, Is.Not.SameAs(inner));
            Assert.That(clonedSetter, Is.Not.SameAs(setter));
            Assert.That(clonedSetter.StartTime, Is.EqualTo(0.5f));
            Assert.That(clonedSetter.Property.Target, Is.SameAs(_target.transform));
            Assert.That(clonedSetter.Value.Vector3Value, Is.EqualTo(Vector3.one));

            clonedSetter.StartTime = 9f;
            Assert.That(setter.StartTime, Is.EqualTo(0.5f));
        }

        [Test]
        public void UpsertSequence_OwnsClonesSoCallerInstancesAreNotShared()
        {
            var provider = _target.AddComponent<SequenceProvider>();
            var setter = Seq.Make.SetPosition(_target.transform, Vector3.one);

            var owned = provider.UpsertSequence("A", new Segment[] { setter }).Segments[0];
            setter.StartTime = 5f;

            Assert.That(owned, Is.Not.SameAs(setter));
            Assert.That(((PropertySetter)owned).StartTime, Is.Zero);
        }
    }
}
