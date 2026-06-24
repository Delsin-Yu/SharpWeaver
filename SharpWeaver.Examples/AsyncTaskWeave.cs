using System.Threading.Tasks;
using SharpWeaver;

namespace SharpWeaver.Examples.AsyncTaskWeave;

/// <summary>User code — async Task target.</summary>
public sealed class AsyncWorker
{
    /// <summary>Simulates async work with one await.</summary>
    public async Task RunAsync()
    {
        Console.WriteLine("async body start");
        await Task.Yield();
        Console.WriteLine("async body end");
    }
}

/// <summary>Weave patch — prefix/postfix around await OriginalBodyAsync().</summary>
public static class AsyncWorkerWeavePatch
{
    [AsyncWeave("SharpWeaver.Examples.AsyncTaskWeave.AsyncWorker.RunAsync(**)", priority: 0)]
    public static async Task RunAsyncWeave(object? instance)
    {
        Console.WriteLine("[async] prefix");
        await WeaveTemplate.OriginalBodyAsync();
        Console.WriteLine("[async] postfix");
    }
}
