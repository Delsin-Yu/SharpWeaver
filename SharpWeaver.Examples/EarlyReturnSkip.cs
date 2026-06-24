using SharpWeaver;

namespace SharpWeaver.Examples.EarlyReturnSkip;

/// <summary>User code — virtual base method (exact weave resolves overrides in the assembly).</summary>
public class CounterBase
{
    /// <summary>Returns the next counter value.</summary>
    /// <returns>Next value.</returns>
    public virtual int GetNext()
    {
        return 1;
    }
}

/// <summary>User code — override that receives the exact-mode weave.</summary>
public sealed class Counter : CounterBase
{
    private int _value;

    /// <inheritdoc />
    public override int GetNext()
    {
        return ++_value;
    }
}

/// <summary>Weave patch — short-circuit with trailing ref return slot (exact mode).</summary>
public static class CounterWeavePatch
{
    /// <summary>When set, the weave returns this value and skips the original body.</summary>
    public static int? CachedValue { get; set; }

    [Weave("SharpWeaver.Examples.EarlyReturnSkip.CounterBase.GetNext()", priority: 0)]
    public static void GetNextWeave(CounterBase instance, ref int returnValue)
    {
        if (CachedValue.HasValue)
        {
            returnValue = CachedValue.Value;
            Console.WriteLine($"[skip] returning cached {returnValue}");
            return;
        }

        WeaveTemplate.OriginalBody();
    }
}
