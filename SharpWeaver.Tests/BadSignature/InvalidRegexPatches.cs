using SharpWeaver;

namespace SharpWeaver.TestFixtures.BadSignature;

/// <summary>ILWeaving weave methods with invalid wildcard patterns (for testing).</summary>
public static class InvalidRegexPatches
{
    /// <summary>Invalid wildcard pattern, should be rejected during scanning.</summary>
    [Weave("***.Method()", priority: 0)]
    public static void BadRegexWeave()
    {
        WeaveTemplate.OriginalBody();
    }
}
