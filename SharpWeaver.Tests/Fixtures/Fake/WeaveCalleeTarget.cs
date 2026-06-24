namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Target type for verifying weave template callee exclusion rules.</summary>
public sealed class WeaveCalleeTarget
{
    /// <summary>Executes target logic.</summary>
    public void Run() => BehavioralState.WeaveCalleeTargetRuns++;
}
