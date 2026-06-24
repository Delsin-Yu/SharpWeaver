using SharpWeaver;

namespace SharpWeaver.Examples.WildcardInstrumentation;

/// <summary>User code — sample service matched by the wildcard.</summary>
public sealed class OrderService
{
    /// <summary>Places an order.</summary>
    /// <param name="itemId">Item identifier.</param>
    public void PlaceOrder(int itemId)
    {
        Console.WriteLine($"Placing order for item {itemId}");
    }
}

/// <summary>User code — another type matched by the same wildcard.</summary>
public sealed class PaymentService
{
    /// <summary>Charges a customer.</summary>
    /// <param name="amount">Amount in cents.</param>
    public void Charge(int amount)
    {
        Console.WriteLine($"Charging {amount} cents");
    }
}

/// <summary>Weave patch — matches methods on types in this namespace; excludes constructors.</summary>
public static class InstrumentationWeavePatch
{
    [Weave("SharpWeaver.Examples.WildcardInstrumentation.*.*(int)", priority: 5)]
    public static void InstrumentWeave(
        object? instance,
        [WeaveTypeName] string typeName,
        [WeaveMethodName] string methodName,
        [WeaveLineNumber] int lineNumber,
        [WeaveFilePath] string filePath)
    {
        Console.WriteLine($"[trace] {typeName}.{methodName} @ {filePath}:{lineNumber}");
        WeaveTemplate.OriginalBody();
    }
}
