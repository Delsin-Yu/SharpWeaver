using GodotTask;

namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Async custom async-like return type weave test target.</summary>
public class AsyncCustomTaskTarget
{
    /// <summary>Single-await async custom task method.</summary>
    public async GDTask SingleAwaitAsync()
    {
        BehavioralState.AsyncBodyRuns++;
        await GDTask.FromResult(0);
        BehavioralState.AsyncPostfixRuns++;
    }

    /// <summary>Multi-await async custom task method with loop-hoisted fields.</summary>
    public async GDTask MultiAwaitAsync()
    {
        BehavioralState.AsyncBodyRuns++;
        var serviceReader = 0;
        while (serviceReader < 2)
        {
            var order = serviceReader;
            serviceReader++;
            BehavioralState.AsyncMidpointRuns = order;
            await GDTask.FromResult(0);
        }

        BehavioralState.AsyncPostfixRuns++;
    }

    /// <summary>Single-await async custom task generic method.</summary>
    public async GDTask<int> GenericAwaitAsync()
    {
        BehavioralState.AsyncBodyRuns++;
        await GDTask.FromResult(0);
        BehavioralState.AsyncPostfixRuns++;
        return 42;
    }

    /// <summary>Open generic async custom task method.</summary>
    public async GDTask<T> GenericMethodResultAsync<T>(T value)
    {
        BehavioralState.AsyncGenericBodyRuns++;
        await GDTask.FromResult(0);
        BehavioralState.AsyncGenericPostfixRuns++;
        return value;
    }

    /// <summary>Synchronously returns a completed custom task (sync weave candidate).</summary>
    public GDTask SyncCompletedAsync() => GDTask.CompletedTask;
}
