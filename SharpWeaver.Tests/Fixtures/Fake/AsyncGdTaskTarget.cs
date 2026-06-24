using GodotTask;

namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Async GDTask weave test target.</summary>
public class AsyncGdTaskTarget
{
    /// <summary>Single-await async GDTask method.</summary>
    public async GDTask SingleAwaitAsync()
    {
        BehavioralState.AsyncBodyRuns++;
        await GDTask.FromResult(0);
        BehavioralState.AsyncPostfixRuns++;
    }

    /// <summary>Multi-await async GDTask method with loop-hoisted fields (close to production GDTask state machine).</summary>
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

    /// <summary>Single-await async GDTask&lt;T&gt; method.</summary>
    public async GDTask<int> GenericAwaitAsync()
    {
        BehavioralState.AsyncBodyRuns++;
        await GDTask.FromResult(0);
        BehavioralState.AsyncPostfixRuns++;
        return 42;
    }

    /// <summary>Open generic async GDTask&lt;T&gt; method.</summary>
    public async GDTask<T> GenericMethodResultAsync<T>(T value)
    {
        BehavioralState.AsyncGenericBodyRuns++;
        await GDTask.FromResult(0);
        BehavioralState.AsyncGenericPostfixRuns++;
        return value;
    }

    /// <summary>Synchronously returns GDTask (not compiler-generated async, should go through sync weave path).</summary>
    public GDTask SyncCompletedAsync() => GDTask.CompletedTask;
}
