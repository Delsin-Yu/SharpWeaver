namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Infrastructure method directly called by weave templates, should be excluded from wildcard matching.</summary>
public static class WeaveCalleeInfra
{
    /// <summary>Increments the weave template infrastructure call count.</summary>
    public static void Touch() => BehavioralState.WeaveCalleeInfraRuns++;
}
