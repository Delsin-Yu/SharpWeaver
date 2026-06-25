using SharpWeaver;
using SharpWeaver.TestFixtures.Fake;

namespace SharpWeaver.TestFixtures;

/// <summary>Call-site weave definitions for testing.</summary>
public static class CallSitePatches
{
    /// <summary>Prefix/postfix around a fake void call while preserving the original call.</summary>
    [WeaveCallSite("SharpWeaver.TestFixtures.Fake.CallSiteCalleeTarget.ExitSimple(int)", priority: 0)]
    public static void ExitSimplePatch()
    {
        BehavioralState.CallSiteTrace.Add("patch-start");
        WeaveTemplate.OriginalBody();
        BehavioralState.CallSiteTrace.Add("patch-end");
    }

    /// <summary>Mutates exit code via <c>ref</c> before executing the original call.</summary>
    /// <param name="exitCode">Callee exit code argument.</param>
    [WeaveCallSite("SharpWeaver.TestFixtures.Fake.CallSiteCalleeTarget.ExitParameter(int)", priority: 0)]
    public static void ExitRefPatch(ref int exitCode)
    {
        BehavioralState.CallSiteTrace.Add("patch-parameter");
        exitCode = 42;
        WeaveTemplate.OriginalBody();
    }

    /// <summary>Skips the original call entirely and allows caller control flow to continue.</summary>
    [WeaveCallSite("SharpWeaver.TestFixtures.Fake.CallSiteCalleeTarget.ExitSkip(int)", priority: 0)]
    public static void ExitSkipPatch()
    {
        BehavioralState.CallSiteTrace.Add("patch-skip-start");
        BehavioralState.CallSiteTrace.Add("patch-skip-end");
    }

    /// <summary>Conditionally skips or executes the original call.</summary>
    /// <param name="exitCode">Callee exit code argument.</param>
    [WeaveCallSite("SharpWeaver.TestFixtures.Fake.CallSiteCalleeTarget.ExitConditional(int)", priority: 0)]
    public static void ExitConditionalPatch(ref int exitCode)
    {
        BehavioralState.CallSiteTrace.Add("patch-conditional");
        if (BehavioralState.CallSiteSkipOriginal)
        {
            return;
        }

        exitCode = 24;
        WeaveTemplate.OriginalBody();
    }

    /// <summary>Overrides a fake non-void call return value without calling it.</summary>
    /// <param name="min">Callee minimum argument.</param>
    /// <param name="max">Callee maximum argument.</param>
    /// <param name="returnValue">Return value slot.</param>
    [WeaveCallSite("SharpWeaver.TestFixtures.Fake.CallSiteCalleeTarget.NextReturn(int, int)", priority: 0)]
    public static void RandomReturnPatch(ref int min, ref int max, ref int returnValue)
    {
        _ = min;
        _ = max;
        BehavioralState.CallSiteTrace.Add("patch-return");
        returnValue = 42;
    }

    /// <summary>Patch that should not be planned because the target is a value-type instance method.</summary>
    /// <param name="value">Callee value argument.</param>
    [WeaveCallSite("SharpWeaver.TestFixtures.Fake.CallSiteStructCalleeTarget.Increment(int)", priority: 0)]
    public static void StructInstancePatch(ref int value)
    {
        value = 42;
        WeaveTemplate.OriginalBody();
    }
}
