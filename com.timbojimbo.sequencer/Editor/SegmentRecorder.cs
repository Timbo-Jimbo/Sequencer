using System;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CustomSegmentRecorderAttribute : Attribute
    {
        public Type InspectedType { get; }

        public CustomSegmentRecorderAttribute(Type inspectedType)
        {
            InspectedType = inspectedType;
        }
    }

    public abstract class SegmentRecorder
    {
        public virtual int Priority => 0;

        public virtual bool CanConsume(Segment segment, BindableProperty property, float time) => false;
        public virtual void Consume(Segment segment, BindableProperty property, ValueContainer value, float time) { }

        public virtual bool CanCreateFor(BindableProperty property) => false;
        public virtual Segment CreateSegment(BindableProperty property, ValueContainer value, float time) => null;
    }
}