using System;

namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Base class for exception wrap capture testing.</summary>
public class ThrowingBase
{
    /// <summary>Virtual method that can be overridden by derived classes.</summary>
    /// <param name="value">Input value; negative values cause the derived implementation to throw.</param>
    public virtual void MayThrow(int value)
    {
    }
}

/// <summary>Override that throws on negative input values.</summary>
public class ThrowingDerived : ThrowingBase
{
    /// <inheritdoc />
    public override void MayThrow(int value)
    {
        if (value < 0)
        {
            throw new InvalidOperationException("boom");
        }

        BehavioralState.MayThrowBodyRuns++;
    }
}
