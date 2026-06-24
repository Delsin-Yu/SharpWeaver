namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Used to verify that a single weave method can have multiple <see cref="WeaveAttribute"/>.</summary>
public sealed class MultiPatternTarget
{
    /// <summary>Alpha probe method.</summary>
    public void Alpha() => BehavioralState.MultiPatternAlphaRuns++;

    /// <summary>Beta probe method.</summary>
    public void Beta() => BehavioralState.MultiPatternBetaRuns++;
}
