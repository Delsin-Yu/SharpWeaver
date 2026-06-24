using SharpWeaver;

namespace SharpWeaver.Examples.WeaveExclude;

/// <summary>User code — included by the wildcard.</summary>
public sealed class IncludedTarget
{
    /// <summary>Runs with the weave prefix.</summary>
    public void Included()
    {
        Console.WriteLine("included body");
    }
}

/// <summary>User code — excluded from the wildcard via <see cref="WeaveExcludeAttribute"/>.</summary>
public sealed class ExcludedTarget
{
    /// <summary>Runs without the weave prefix.</summary>
    public void Excluded()
    {
        Console.WriteLine("excluded body");
    }
}

/// <summary>Weave patch — broad match with explicit exclusions.</summary>
public static class ExcludeWeavePatch
{
    [Weave("SharpWeaver.Examples.WeaveExclude.*Target.*uded(**)", priority: 5)]
    [WeaveExclude("SharpWeaver.Examples.WeaveExclude.ExcludedTarget.Excluded(**)")]
    public static void ExcludeWeave(object? instance)
    {
        Console.WriteLine("[prefix] before original body");
        WeaveTemplate.OriginalBody();
    }
}
