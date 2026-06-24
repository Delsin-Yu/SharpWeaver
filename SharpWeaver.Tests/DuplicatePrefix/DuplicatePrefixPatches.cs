using SharpWeaver;

namespace SharpWeaver.TestFixtures.DuplicatePrefix;

/// <summary>Two ILWeaving weave methods on the same target signature, used to verify onion composition discovery order.</summary>
public static class DuplicateWeavePatches
{
    /// <summary>
    /// First weave method (inner onion layer): prefix <c>weaveA</c> → original body → postfix <c>weaveA_post</c>.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    [Weave("SharpWeaver.TestFixtures.DuplicatePrefix.DuplicateTarget.TargetMethod()", priority: 0)]
    public static void WeaveA(DuplicateTarget instance)
    {
        _ = instance;
        DuplicateTarget.CompositionTrace.Add("weaveA");
        WeaveTemplate.OriginalBody();
        DuplicateTarget.CompositionTrace.Add("weaveA_post");
    }

    /// <summary>
    /// Second weave method (outer onion layer): prefix <c>weaveB</c> → WeaveA composite → postfix <c>weaveB_post</c>.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    [Weave("SharpWeaver.TestFixtures.DuplicatePrefix.DuplicateTarget.TargetMethod()", priority: 1)]
    public static void WeaveB(DuplicateTarget instance)
    {
        _ = instance;
        DuplicateTarget.CompositionTrace.Add("weaveB");
        WeaveTemplate.OriginalBody();
        DuplicateTarget.CompositionTrace.Add("weaveB_post");
    }
}
