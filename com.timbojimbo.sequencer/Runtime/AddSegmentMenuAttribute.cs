using System;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AddSegmentMenuAttribute : Attribute
{
    public string MenuName { get; }

    public AddSegmentMenuAttribute(string menuName)
    {
        MenuName = menuName;
    }
}
