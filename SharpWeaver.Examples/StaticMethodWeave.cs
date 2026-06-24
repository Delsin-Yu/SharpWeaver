using SharpWeaver;

namespace SharpWeaver.Examples.StaticMethodWeave;

/// <summary>User code — static utility method.</summary>
public static class MathUtil
{
    /// <summary>Adds two numbers.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>Sum.</returns>
    public static int Add(int a, int b)
    {
        return a + b;
    }
}

/// <summary>Weave patch — wildcard on a static method; instance parameter becomes ldnull.</summary>
public static class MathUtilWeavePatch
{
    [Weave("SharpWeaver.Examples.StaticMethodWeave.MathUtil.Add(**)", priority: 0)]
    public static void AddWeave(object? instance)
    {
        Console.WriteLine("[static] Add invoked");
        WeaveTemplate.OriginalBody();
    }
}
