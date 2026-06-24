namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Base class for ILWeaving spike testing (independent of FakeBase hierarchy).</summary>
public class WeaveBase
{
    /// <summary>Virtual method that can be overridden by derived classes.</summary>
    /// <param name="value">Input value.</param>
    public virtual void WeaveWork(int value)
    {
    }
}

/// <summary>Leaf class that overrides <see cref="WeaveBase.WeaveWork(int)"/>, used for ILWeaving spike behavior verification.</summary>
public class WeaveLeaf : WeaveBase
{
    /// <inheritdoc />
    public override void WeaveWork(int value)
    {
        BehavioralState.WeaveWorkBodyRuns++;
        BehavioralState.WeaveWorkTrace.Add("body");
    }
}
