using System;

namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Custom attribute for testing, used to verify wildcard matching exclusion of <see cref="Attribute"/> derived types.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FakeMetadataAttribute(string label) : Attribute
{
    /// <summary>Gets the label text.</summary>
    public string Label { get; } = label;
}
