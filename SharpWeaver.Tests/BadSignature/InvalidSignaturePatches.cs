using SharpWeaver;

namespace SharpWeaver.TestFixtures.BadSignature;

/// <summary>ILWeaving weave methods with unresolvable target signatures (for testing).</summary>
public static class InvalidSignaturePatches
{
    /// <summary>ILWeaving weave method referencing a non-existent type (target signature cannot be resolved).</summary>
    [Weave("NoSuch.Namespace.NoSuchType.NoMethod(int)", priority: 0)]
    public static void BadWeave()
    {
        WeaveTemplate.OriginalBody();
    }
}
