using SharpWeaver;

namespace SharpWeaver.Examples.MultiTargetWeave;

/// <summary>User code — first matched method.</summary>
public sealed class AlphaService
{
    /// <summary>Alpha entry point.</summary>
    public void Run()
    {
        Console.WriteLine("alpha");
    }
}

/// <summary>User code — second matched method.</summary>
public sealed class BetaService
{
    /// <summary>Beta entry point.</summary>
    public void Run()
    {
        Console.WriteLine("beta");
    }
}

/// <summary>Weave patch — shared template for two exact signatures.</summary>
public static class SharedWeavePatch
{
    [Weave("SharpWeaver.Examples.MultiTargetWeave.AlphaService.Run(**)", priority: 0)]
    [Weave("SharpWeaver.Examples.MultiTargetWeave.BetaService.Run(**)", priority: 0)]
    public static void SharedRunWeave(
        object? instance,
        [WeaveMethodName] string methodName)
    {
        Console.WriteLine($"[shared] entering {methodName}");
        WeaveTemplate.OriginalBody();
    }
}
