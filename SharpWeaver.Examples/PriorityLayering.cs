using SharpWeaver;

namespace SharpWeaver.Examples.PriorityLayering;

/// <summary>User code — single method receiving two layers of weaving.</summary>
public sealed class LayeredService
{
    /// <summary>Core business logic.</summary>
    public void Execute()
    {
        Console.WriteLine("core");
    }
}

/// <summary>Weave patch — inner layer (lower priority, closer to the original body).</summary>
public static class InnerWeavePatch
{
    [Weave("SharpWeaver.Examples.PriorityLayering.LayeredService.Execute(**)", priority: 0)]
    public static void InnerWeave(object? instance)
    {
        Console.WriteLine("[inner] enter");
        WeaveTemplate.OriginalBody();
        Console.WriteLine("[inner] leave");
    }
}

/// <summary>Weave patch — outer layer (higher priority, wraps outside the inner weave).</summary>
public static class OuterWeavePatch
{
    [Weave("SharpWeaver.Examples.PriorityLayering.LayeredService.Execute(**)", priority: 10)]
    public static void OuterWeave(object? instance)
    {
        Console.WriteLine("[outer] enter");
        WeaveTemplate.OriginalBody();
        Console.WriteLine("[outer] leave");
    }
}
