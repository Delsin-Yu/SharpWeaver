namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Base class for override matching tests without external framework dependencies.</summary>
public class FakeBase
{
    /// <summary>Virtual method that can be overridden by derived classes.</summary>
    /// <param name="value">Input value.</param>
    public virtual void DoWork(int value)
    {
    }

    /// <summary>Ordinary method unrelated to override matching (should not be selected by the weaver).</summary>
    /// <param name="value">Input value.</param>
    /// <returns>Twice the input value.</returns>
    public int UnrelatedHelper(int value)
    {
        return value * 2;
    }
}

/// <summary>Derived class that directly overrides <see cref="FakeBase.DoWork(int)"/>.</summary>
public class FakeDerived : FakeBase
{
    /// <inheritdoc />
    public override void DoWork(int value)
    {
    }
}

/// <summary>Intermediate override, used to verify override chain matching.</summary>
public class FakeIntermediate : FakeBase
{
    /// <inheritdoc />
    public override void DoWork(int value)
    {
        base.DoWork(value);
    }
}

/// <summary>Leaf class that overrides the intermediate method.</summary>
public class FakeLeaf : FakeIntermediate
{
    /// <inheritdoc />
    public override void DoWork(int value)
    {
        BehavioralState.DoWorkBodyRuns++;
        BehavioralState.DoWorkTrace.Add("body");
    }
}
