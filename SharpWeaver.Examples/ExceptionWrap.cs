using SharpWeaver;

namespace SharpWeaver.Examples.ExceptionWrap;

/// <summary>User code — may throw.</summary>
public sealed class Calculator
{
    /// <summary>Divides two integers.</summary>
    /// <param name="a">Dividend.</param>
    /// <param name="b">Divisor.</param>
    /// <returns>Quotient.</returns>
    public int Divide(int a, int b)
    {
        return a / b;
    }
}

/// <summary>Weave patch — log exceptions, then rethrow.</summary>
public static class CalculatorWeavePatch
{
    [Weave("SharpWeaver.Examples.ExceptionWrap.Calculator.Divide(**)", priority: 0)]
    public static void DivideWeave(object? instance)
    {
        try
        {
            WeaveTemplate.OriginalBody();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[error] Divide failed: {ex.Message}");
            throw;
        }
    }
}
