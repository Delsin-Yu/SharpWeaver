using System;
using System.Collections.Generic;

namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Observable state for post-weave behavior testing.</summary>
public static class BehavioralState
{
    /// <summary>Prefix skip test: when non-null, the prefix returns this value early.</summary>
    public static int? IntReturnPrefixValue { get; set; }

    /// <summary>Number of times an external-base tick override body executed.</summary>
    public static int TickBodyRuns { get; set; }

    /// <summary>Number of times <see cref="FakeLeaf.DoWork(int)"/> method body executed.</summary>
    public static int DoWorkBodyRuns { get; set; }

    /// <summary>Number of times <c>FakeBase.DoWork(int)</c> postfix patch executed.</summary>
    public static int DoWorkPostfixRuns { get; set; }

    /// <summary>Records <c>DoWork</c> execution order (body / postfix).</summary>
    public static List<string> DoWorkTrace { get; } = [];

    /// <summary>Number of times <see cref="ThrowingDerived.MayThrow(int)"/> method body executed successfully.</summary>
    public static int MayThrowBodyRuns { get; set; }

    /// <summary>Number of times the exception wrap patch was invoked.</summary>
    public static int ExceptionWrapHandlerRuns { get; set; }

    /// <summary>The most recent exception message caught by the exception wrap patch.</summary>
    public static string? LastCaughtExceptionMessage { get; set; }

    /// <summary>Number of times <see cref="WeaveLeaf.WeaveWork(int)"/> method body executed (ILWeaving spike only).</summary>
    public static int WeaveWorkBodyRuns { get; set; }

    /// <summary>Number of times ILWeaving weave postfix code executed (ILWeaving spike only).</summary>
    public static int WeaveWorkPostfixRuns { get; set; }

    /// <summary>Records <c>WeaveWork</c> execution order (body / weave_postfix) (ILWeaving spike only).</summary>
    public static List<string> WeaveWorkTrace { get; } = [];

    /// <summary>Number of times <see cref="RegexPanelTarget._OnPanelOpen"/> method body executed.</summary>
    public static int RegexPanelOpenRuns { get; set; }

    /// <summary>Number of times <see cref="RegexPanelTarget.OtherMethod"/> method body executed.</summary>
    public static int RegexOtherMethodRuns { get; set; }

    /// <summary>Captured method name injected by regex weaving.</summary>
    public static string? RegexCapturedMethodName { get; set; }

    /// <summary>Captured type name injected by regex weaving.</summary>
    public static string? RegexCapturedTypeName { get; set; }

    /// <summary>Number of times <see cref="WeaveCalleeInfra.Touch"/> executed.</summary>
    public static int WeaveCalleeInfraRuns { get; set; }

    /// <summary>Number of times <see cref="WeaveCalleeTarget.Run"/> method body executed.</summary>
    public static int WeaveCalleeTargetRuns { get; set; }

    /// <summary>Number of times <see cref="MultiPatternTarget.Alpha"/> method body executed.</summary>
    public static int MultiPatternAlphaRuns { get; set; }

    /// <summary>Number of times <see cref="MultiPatternTarget.Beta"/> method body executed.</summary>
    public static int MultiPatternBetaRuns { get; set; }

    /// <summary>The most recent method name captured by multi-pattern regex weaving.</summary>
    public static string? MultiPatternLastMethodName { get; set; }

    /// <summary>Number of times <see cref="BranchReturnTarget.Convert"/> method body executed.</summary>
    public static int BranchReturnBodyRuns { get; set; }

    /// <summary>Number of times <see cref="InitPropertyTarget.set_Value"/> method body executed.</summary>
    public static int InitPropertySetterBodyRuns { get; set; }

    /// <summary>Number of times <see cref="WildcardExcludeTarget.Included"/> method body executed.</summary>
    public static int WildcardExcludeIncludedBodyRuns { get; set; }

    /// <summary>Number of times <see cref="WildcardExcludeTarget.Excluded"/> method body executed.</summary>
    public static int WildcardExcludeExcludedBodyRuns { get; set; }

    /// <summary>Number of times the weave prefix executed for wildcard exclusion test.</summary>
    public static int WildcardExcludeWeavePrefixRuns { get; set; }

    /// <summary>Number of times the open generic method target's body executed.</summary>
    public static int GenericMethodBodyRuns { get; set; }

    /// <summary>Number of times the generic declaring type target's body executed.</summary>
    public static int GenericDeclaringTypeBodyRuns { get; set; }

    /// <summary>Number of times the non-generic method body (bypassed by generic target) executed.</summary>
    public static int GenericNonGenericBodyRuns { get; set; }

    /// <summary>Number of times the generic-aware sync weave template executed.</summary>
    public static int GenericWeaveRuns { get; set; }

    /// <summary>Number of times the non-generic sync weave template executed.</summary>
    public static int GenericNonGenericWeaveRuns { get; set; }

    /// <summary>The most recent target method name captured by sync generic weaving.</summary>
    public static string? GenericCapturedMethodName { get; set; }

    /// <summary>The most recent generic parameter type names captured by sync generic weaving.</summary>
    public static string[] GenericCapturedTypeParamNames { get; set; } = [];

    /// <summary>Records call-site weave execution order.</summary>
    public static List<string> CallSiteTrace { get; } = [];

    /// <summary>Exit codes observed by fake call-site callees.</summary>
    public static List<int> CallSiteExitCodes { get; } = [];

    /// <summary>Whether the conditional call-site template should skip the original call.</summary>
    public static bool CallSiteSkipOriginal { get; set; }

    /// <summary>Number of times the fake non-void call-site callee executed.</summary>
    public static int CallSiteNextRuns { get; set; }

    /// <summary>Number of times the async target method body inline segment executed.</summary>
    public static int AsyncBodyRuns { get; set; }

    /// <summary>Number of times the async target method body's native postfix after await executed.</summary>
    public static int AsyncPostfixRuns { get; set; }

    /// <summary>Number of times the async weave prefix executed.</summary>
    public static int AsyncPrefixRuns { get; set; }

    /// <summary>Number of times the async weave postfix executed.</summary>
    public static int AsyncWeavePostfixRuns { get; set; }

    /// <summary>Number of times the hoisted <see cref="IDisposable"/> from the async weave template was disposed.</summary>
    public static int AsyncDisposeRuns { get; set; }

    /// <summary>Records the execution order of async weaving and target method body.</summary>
    public static List<string> AsyncTrace { get; } = [];

    /// <summary>Records one async weave postfix execution via an ordinary method call.</summary>
    public static void RecordAsyncWeavePostfix() => AsyncWeavePostfixRuns++;

    /// <summary>Records one async weave postfix execution via an ordinary method call with parameters.</summary>
    public static void RecordAsyncWeavePostfix(string typeName, string methodName)
    {
        AsyncTrace.Add(typeName);
        AsyncTrace.Add(methodName);
        AsyncWeavePostfixRuns++;
    }

    /// <summary>Number of times the native midpoint after the first await in the async target method body executed.</summary>
    public static int AsyncMidpointRuns { get; set; }

    /// <summary>Captured method name injected by async weaving.</summary>
    public static string? AsyncCapturedMethodName { get; set; }

    /// <summary>Number of times the open generic async target method body executed.</summary>
    public static int AsyncGenericBodyRuns { get; set; }

    /// <summary>Number of times the native code after await in the open generic async target executed.</summary>
    public static int AsyncGenericPostfixRuns { get; set; }

    /// <summary>Number of times the generic-aware async weave prefix executed.</summary>
    public static int AsyncGenericPrefixRuns { get; set; }

    /// <summary>Number of times the generic-aware async weave postfix executed.</summary>
    public static int AsyncGenericWeavePostfixRuns { get; set; }

    /// <summary>The most recent target method name captured by async generic weaving.</summary>
    public static string? AsyncGenericCapturedMethodName { get; set; }

    /// <summary>The most recent generic parameter type names captured by async generic weaving.</summary>
    public static string[] AsyncGenericCapturedTypeParamNames { get; set; } = [];

    /// <summary>Resets all counters and traces.</summary>
    public static void Reset()
    {
        IntReturnPrefixValue = null;
        TickBodyRuns = 0;
        DoWorkBodyRuns = 0;
        DoWorkPostfixRuns = 0;
        DoWorkTrace.Clear();
        MayThrowBodyRuns = 0;
        ExceptionWrapHandlerRuns = 0;
        LastCaughtExceptionMessage = null;
        WeaveWorkBodyRuns = 0;
        WeaveWorkPostfixRuns = 0;
        WeaveWorkTrace.Clear();
        RegexPanelOpenRuns = 0;
        RegexOtherMethodRuns = 0;
        RegexCapturedMethodName = null;
        RegexCapturedTypeName = null;
        WeaveCalleeInfraRuns = 0;
        WeaveCalleeTargetRuns = 0;
        MultiPatternAlphaRuns = 0;
        MultiPatternBetaRuns = 0;
        MultiPatternLastMethodName = null;
        BranchReturnBodyRuns = 0;
        InitPropertySetterBodyRuns = 0;
        WildcardExcludeIncludedBodyRuns = 0;
        WildcardExcludeExcludedBodyRuns = 0;
        WildcardExcludeWeavePrefixRuns = 0;
        GenericMethodBodyRuns = 0;
        GenericDeclaringTypeBodyRuns = 0;
        GenericNonGenericBodyRuns = 0;
        GenericWeaveRuns = 0;
        GenericNonGenericWeaveRuns = 0;
        GenericCapturedMethodName = null;
        GenericCapturedTypeParamNames = [];
        CallSiteTrace.Clear();
        CallSiteExitCodes.Clear();
        CallSiteSkipOriginal = false;
        CallSiteNextRuns = 0;
        AsyncBodyRuns = 0;
        AsyncPostfixRuns = 0;
        AsyncPrefixRuns = 0;
        AsyncWeavePostfixRuns = 0;
        AsyncDisposeRuns = 0;
        AsyncTrace.Clear();
        AsyncMidpointRuns = 0;
        AsyncCapturedMethodName = null;
        AsyncGenericBodyRuns = 0;
        AsyncGenericPostfixRuns = 0;
        AsyncGenericPrefixRuns = 0;
        AsyncGenericWeavePostfixRuns = 0;
        AsyncGenericCapturedMethodName = null;
        AsyncGenericCapturedTypeParamNames = [];
    }
}
