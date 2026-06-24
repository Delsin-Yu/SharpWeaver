namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Wildcard target for verifying <see cref="SharpWeaver.WeaveExcludeAttribute"/>.</summary>
public sealed class WildcardExcludeTarget
{
    /// <summary>Method that should be hit by a broad wildcard weave.</summary>
    public void Included() => BehavioralState.WildcardExcludeIncludedBodyRuns++;

    /// <summary>Method that should be excluded from a broad wildcard weave by an exclusion pattern.</summary>
    public void Excluded() => BehavioralState.WildcardExcludeExcludedBodyRuns++;
}
