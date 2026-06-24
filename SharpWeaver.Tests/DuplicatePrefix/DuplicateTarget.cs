using System.Collections.Generic;

namespace SharpWeaver.TestFixtures.DuplicatePrefix;

/// <summary>Virtual method base class for testing multi-weave composition order.</summary>
public class DuplicateTarget
{
    /// <summary>
    /// Trace list recording the execution order of weave postfixes, used to verify onion discovery order during composition.
    /// </summary>
    public static List<string> CompositionTrace { get; } = new List<string>();

    /// <summary>Resets the composition trace list.</summary>
    public static void Reset() => CompositionTrace.Clear();

    /// <summary>Virtual method that can be overridden by derived classes.</summary>
    public virtual void TargetMethod()
    {
    }
}

/// <summary>Derived class that overrides <see cref="DuplicateTarget.TargetMethod"/>.</summary>
public class DuplicateOverride : DuplicateTarget
{
    /// <inheritdoc />
    public override void TargetMethod()
    {
        CompositionTrace.Add("body");
    }
}
