using SharpWeaver;

namespace SharpWeaver.Examples.VirtualOverrideWeave;

/// <summary>User code — virtual base method.</summary>
public class WorkerBase
{
    /// <summary>Virtual work entry point.</summary>
    /// <param name="units">Work units.</param>
    public virtual void DoWork(int units)
    {
        Console.WriteLine($"base work: {units}");
    }
}

/// <summary>User code — override in a derived type.</summary>
public sealed class WorkerDerived : WorkerBase
{
    /// <inheritdoc />
    public override void DoWork(int units)
    {
        Console.WriteLine($"derived work: {units}");
    }
}

/// <summary>Weave patch — targets the base signature; override receives the same weave.</summary>
public static class WorkerWeavePatch
{
    [Weave("SharpWeaver.Examples.VirtualOverrideWeave.WorkerBase.DoWork(int)", priority: 0)]
    public static void DoWorkWeave(WorkerBase instance, ref int units)
    {
        Console.WriteLine("[weave] before DoWork");
        WeaveTemplate.OriginalBody();
        Console.WriteLine("[weave] after DoWork");
    }
}
