using SharpWeaver.TestFixtures.ExternalBase;

namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Fixture type that overrides an external base tick method.</summary>
public class DerivedTickNode : TickHost
{
    /// <inheritdoc />
    public override void Tick(double delta)
    {
        _ = delta;
        BehavioralState.TickBodyRuns++;
    }
}
