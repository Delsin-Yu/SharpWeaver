using SharpWeaver;

namespace SharpWeaver.Examples.PrefixPostfixLogging;

/// <summary>User code — method to instrument.</summary>
public sealed class Greeter
{
    /// <summary>Prints a greeting.</summary>
    /// <param name="name">Name to greet.</param>
    public void SayHello(string name)
    {
        Console.WriteLine($"Hello, {name}!");
    }
}

/// <summary>Weave patch — wildcard match with prefix and postfix logging.</summary>
public static class GreeterWeavePatch
{
    [Weave("SharpWeaver.Examples.PrefixPostfixLogging.Greeter.SayHello(**)", priority: 0)]
    public static void SayHelloWeave(object? instance)
    {
        Console.WriteLine("[enter] SayHello");
        WeaveTemplate.OriginalBody();
        Console.WriteLine("[leave] SayHello");
    }
}
