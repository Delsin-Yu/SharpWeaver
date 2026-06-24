namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Return value base class for prefix skip branch testing.</summary>
public class IntReturnBase
{
    /// <summary>Virtual method that returns a fixed value.</summary>
    /// <returns>Default value 99.</returns>
    public virtual int GetValue()
    {
        return 99;
    }
}

/// <summary>Derived class that overrides <see cref="IntReturnBase.GetValue"/>.</summary>
public class IntReturnDerived : IntReturnBase
{
    /// <inheritdoc />
    public override int GetValue()
    {
        return 42;
    }
}
