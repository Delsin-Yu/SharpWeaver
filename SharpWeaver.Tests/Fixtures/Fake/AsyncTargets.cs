using System.Threading.Tasks;

namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Async Task weave test target.</summary>
public class AsyncTaskTarget
{
    /// <summary>Single-await async Task method.</summary>
    public async Task SingleAwaitAsync()
    {
        BehavioralState.AsyncBodyRuns++;
        await Task.Yield();
        BehavioralState.AsyncPostfixRuns++;
    }

    /// <summary>Multi-await async Task method.</summary>
    public async Task MultiAwaitAsync()
    {
        BehavioralState.AsyncBodyRuns++;
        await Task.Yield();
        BehavioralState.AsyncMidpointRuns++;
        await Task.Yield();
        BehavioralState.AsyncPostfixRuns++;
    }

    /// <summary>Synchronously returns a Task (not compiler-generated async, should go through sync weave path).</summary>
    public Task SyncCompletedAsync() => Task.CompletedTask;

    /// <summary>Single-await async Task&lt;T&gt; method.</summary>
    public async Task<int> GenericAwaitAsync()
    {
        BehavioralState.AsyncBodyRuns++;
        await Task.Yield();
        BehavioralState.AsyncPostfixRuns++;
        return 42;
    }

    /// <summary>Open generic async Task method.</summary>
    public async Task GenericMethodAsync<T>(T value)
    {
        _ = value;
        BehavioralState.AsyncGenericBodyRuns++;
        await Task.Yield();
        BehavioralState.AsyncGenericPostfixRuns++;
    }
}

/// <summary>Verifies that async weave postfixes in the form of ordinary method calls are not missed by postfix boundary detection.</summary>
public class AsyncOrdinaryPostfixTarget
{
    /// <summary>Single-await async Task method.</summary>
    public async Task SingleAwaitAsync()
    {
        BehavioralState.AsyncBodyRuns++;
        await Task.Yield();
        BehavioralState.AsyncPostfixRuns++;
    }
}

/// <summary>Verifies that postfixes after using statements in async weave templates are not skipped.</summary>
public class AsyncUsingStatementPostfixTarget
{
    /// <summary>Single-await async Task method.</summary>
    public async Task SingleAwaitAsync()
    {
        BehavioralState.AsyncBodyRuns++;
        await Task.Yield();
        BehavioralState.AsyncPostfixRuns++;
    }
}

/// <summary>Verifies that user branches in multi-state async methods are not misidentified as initial state dispatch.</summary>
public class AsyncSwitchDispatchTarget
{
    /// <summary>Async Task method with user <c>goto</c> and multiple awaits.</summary>
    public async Task BranchBeforeFirstAwaitAsync(bool skip)
    {
        if (skip) { goto End; }

        BehavioralState.AsyncTrace.Add("body");
        BehavioralState.AsyncBodyRuns++;
        await Task.Yield();
        BehavioralState.AsyncMidpointRuns++;
        await Task.Yield();
        BehavioralState.AsyncPostfixRuns++;

    End:
        BehavioralState.AsyncTrace.Add("end");
    }
}
