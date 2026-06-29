using System;
using System.Threading.Tasks;
using SharpWeaver;
using SharpWeaver.TestFixtures.Fake;

namespace SharpWeaver.TestFixtures;

/// <summary>AsyncILWeaving weave method definitions for testing.</summary>
public static class ValidAsyncPatches
{
    /// <summary>Verifies that hoisted using variables in async weave templates are disposed by the target state machine.</summary>
    public readonly struct AsyncDisposeProbe : IDisposable
    {
        /// <summary>Records one dispose.</summary>
        public void Dispose() => BehavioralState.AsyncDisposeRuns++;
    }

    /// <summary>
    /// Wildcard weave <c>AsyncTaskTarget.*Async(**)</c>: records prefix, awaits original body, then executes postfix.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    /// <param name="typeName">Injected target type fully qualified name.</param>
    /// <param name="methodName">Injected target method name.</param>
    [AsyncWeave("SharpWeaver.TestFixtures.Fake.AsyncTaskTarget.*Async(**)", priority: 5)]
    public static async Task TaskWildcardWeave(
        object? instance,
        [WeaveTypeName] string typeName,
        [WeaveMethodName] string methodName)
    {
        _ = instance;
        _ = typeName;
        BehavioralState.AsyncPrefixRuns++;
        BehavioralState.AsyncCapturedMethodName = methodName;
        await WeaveTemplate.OriginalBodyAsync();
        BehavioralState.AsyncWeavePostfixRuns++;
    }

    /// <summary>
    /// Generic-aware wildcard weave <c>AsyncTaskTarget.*Async(**)</c>: records open generic parameters, then awaits original body.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    /// <param name="genericTypeParams">Open generic parameters visible on the target method.</param>
    /// <param name="methodName">Injected target method name.</param>
    [AsyncWeave("SharpWeaver.TestFixtures.Fake.AsyncTaskTarget.*Async(**)", priority: 5, genericWeave: true)]
    public static async Task TaskGenericWildcardWeave(
        object? instance,
        [WeaveTypeParams] Type[] genericTypeParams,
        [WeaveMethodName] string methodName)
    {
        _ = instance;
        BehavioralState.AsyncGenericPrefixRuns++;
        BehavioralState.AsyncGenericCapturedMethodName = methodName;
        var names = new string[genericTypeParams.Length];
        for (var i = 0; i < genericTypeParams.Length; i++)
        {
            names[i] = genericTypeParams[i].Name;
        }

        BehavioralState.AsyncGenericCapturedTypeParamNames = names;
        await WeaveTemplate.OriginalBodyAsync();
    }

    /// <summary>
    /// Wildcard weave <c>AsyncCustomTaskTarget.*Async(**)</c>: verifies custom async-like return type rewriting.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    [AsyncWeave("SharpWeaver.TestFixtures.Fake.AsyncCustomTaskTarget.*Async(**)", priority: 5)]
    public static async Task CustomTaskWildcardWeave(object? instance)
    {
        _ = instance;
        BehavioralState.AsyncPrefixRuns++;
        using var probe = new AsyncDisposeProbe();
        await WeaveTemplate.OriginalBodyAsync();
        BehavioralState.AsyncWeavePostfixRuns++;
    }

    /// <summary>
    /// Generic-aware wildcard weave for <c>AsyncCustomTaskTarget.GenericMethodResultAsync</c>.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    /// <param name="genericTypeParams">Open generic parameters visible on the target method.</param>
    [AsyncWeave("SharpWeaver.TestFixtures.Fake.AsyncCustomTaskTarget.GenericMethodResultAsync(**)", priority: 5, genericWeave: true)]
    public static async Task CustomTaskGenericWildcardWeave(
        object? instance,
        [WeaveTypeParams] Type[] genericTypeParams)
    {
        _ = instance;
        BehavioralState.AsyncGenericPrefixRuns++;
        var names = new string[genericTypeParams.Length];
        for (var i = 0; i < genericTypeParams.Length; i++)
        {
            names[i] = genericTypeParams[i].Name;
        }

        BehavioralState.AsyncGenericCapturedTypeParamNames = names;
        await WeaveTemplate.OriginalBodyAsync();
    }

    /// <summary>Exact weave for <c>AsyncTaskTarget.MultiAwaitAsync()</c> (does not overlap with wildcards).</summary>
    /// <param name="instance">Target instance.</param>
    [AsyncWeave("SharpWeaver.TestFixtures.Fake.AsyncTaskTarget.MultiAwaitAsync()", priority: 5)]
    public static async Task TaskExactWeave(object? instance)
    {
        _ = instance;
        BehavioralState.AsyncPrefixRuns++;
        await WeaveTemplate.OriginalBodyAsync();
        BehavioralState.AsyncWeavePostfixRuns++;
    }

    /// <summary>Wildcard weave <c>AsyncOrdinaryPostfixTarget.*Async(**)</c>: postfix only calls an ordinary method.</summary>
    /// <param name="instance">Target instance.</param>
    /// <param name="typeName">Injected target type fully qualified name.</param>
    /// <param name="methodName">Injected target method name.</param>
    [AsyncWeave("SharpWeaver.TestFixtures.Fake.AsyncOrdinaryPostfixTarget.*Async(**)", priority: 5)]
    public static async Task TaskOrdinaryMethodPostfixWeave(
        object? instance,
        [WeaveTypeName] string typeName,
        [WeaveMethodName] string methodName)
    {
        _ = instance;
        BehavioralState.AsyncPrefixRuns++;
        await WeaveTemplate.OriginalBodyAsync();
        BehavioralState.RecordAsyncWeavePostfix(typeName, methodName);
    }

    /// <summary>Wildcard weave <c>AsyncUsingStatementPostfixTarget.*Async(**)</c>: postfix is located after a using statement.</summary>
    /// <param name="instance">Target instance.</param>
    [AsyncWeave("SharpWeaver.TestFixtures.Fake.AsyncUsingStatementPostfixTarget.*Async(**)", priority: 5)]
    public static async Task TaskUsingStatementPostfixWeave(object? instance)
    {
        _ = instance;
        BehavioralState.AsyncPrefixRuns++;
        using (new AsyncDisposeProbe())
            await WeaveTemplate.OriginalBodyAsync();
        BehavioralState.AsyncWeavePostfixRuns++;
    }

    /// <summary>Wildcard weave <c>AsyncSwitchDispatchTarget.*Async(**)</c>: records prefix/body/postfix order.</summary>
    /// <param name="instance">Target instance.</param>
    [AsyncWeave("SharpWeaver.TestFixtures.Fake.AsyncSwitchDispatchTarget.*Async(**)", priority: 5)]
    public static async Task TaskSwitchDispatchWeave(object? instance)
    {
        _ = instance;
        BehavioralState.AsyncTrace.Add("prefix");
        BehavioralState.AsyncPrefixRuns++;
        await WeaveTemplate.OriginalBodyAsync();
        BehavioralState.AsyncTrace.Add("weave_postfix");
        BehavioralState.AsyncWeavePostfixRuns++;
    }
}
