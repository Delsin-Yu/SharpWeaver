namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Fake callees used by call-site weave tests.</summary>
public static class CallSiteCalleeTarget
{
    /// <summary>Records a simple void call.</summary>
    /// <param name="exitCode">Observed exit code.</param>
    public static void ExitSimple(int exitCode)
    {
        BehavioralState.CallSiteTrace.Add($"original-simple:{exitCode}");
        BehavioralState.CallSiteExitCodes.Add(exitCode);
    }

    /// <summary>Records a void call whose argument can be changed by a call-site template.</summary>
    /// <param name="exitCode">Observed exit code.</param>
    public static void ExitParameter(int exitCode)
    {
        BehavioralState.CallSiteTrace.Add($"original-parameter:{exitCode}");
        BehavioralState.CallSiteExitCodes.Add(exitCode);
    }

    /// <summary>Records a void call that can be skipped by a call-site template.</summary>
    /// <param name="exitCode">Observed exit code.</param>
    public static void ExitSkip(int exitCode)
    {
        BehavioralState.CallSiteTrace.Add($"original-skip:{exitCode}");
        BehavioralState.CallSiteExitCodes.Add(exitCode);
    }

    /// <summary>Records a void call whose execution is conditional after weaving.</summary>
    /// <param name="exitCode">Observed exit code.</param>
    public static void ExitConditional(int exitCode)
    {
        BehavioralState.CallSiteTrace.Add($"original-conditional:{exitCode}");
        BehavioralState.CallSiteExitCodes.Add(exitCode);
    }

    /// <summary>Returns a value that can be replaced by a call-site template.</summary>
    /// <param name="min">Minimum value.</param>
    /// <param name="max">Maximum value.</param>
    /// <returns>The unpatched fake random value.</returns>
    public static int NextReturn(int min, int max)
    {
        BehavioralState.CallSiteTrace.Add($"original-next:{min}:{max}");
        BehavioralState.CallSiteNextRuns++;
        return 7;
    }
}

/// <summary>Value-type callee used to verify unsupported receiver slots are skipped.</summary>
public struct CallSiteStructCalleeTarget
{
    /// <summary>Instance method whose hidden receiver is passed by managed address in IL.</summary>
    /// <param name="value">Input value.</param>
    /// <returns>The incremented value.</returns>
    public int Increment(int value) => value + 1;
}

/// <summary>Caller methods containing fake call sites to be woven.</summary>
public sealed class CallSiteCallerTarget
{
    /// <summary>Calls a void callee with prefix and postfix call-site weaving.</summary>
    public void RunSimple()
    {
        BehavioralState.CallSiteTrace.Add("caller-before");
        CallSiteCalleeTarget.ExitSimple(0);
        BehavioralState.CallSiteTrace.Add("caller-after");
    }

    /// <summary>Calls a void callee whose argument is mutated by call-site weaving.</summary>
    public void RunParameterMutation()
    {
        CallSiteCalleeTarget.ExitParameter(0);
    }

    /// <summary>Calls a void callee that is skipped by call-site weaving.</summary>
    public void RunSkipVoid()
    {
        CallSiteCalleeTarget.ExitSkip(0);
        BehavioralState.CallSiteTrace.Add("caller-continued");
    }

    /// <summary>Calls a void callee that may be skipped depending on test state.</summary>
    public void RunConditional()
    {
        CallSiteCalleeTarget.ExitConditional(0);
        BehavioralState.CallSiteTrace.Add("caller-continued");
    }

    /// <summary>Calls a non-void callee whose return value is replaced by call-site weaving.</summary>
    /// <returns>The value observed by the caller.</returns>
    public int RunReturnReplacement()
    {
        return CallSiteCalleeTarget.NextReturn(0, 100);
    }

    /// <summary>Calls a value-type instance method that call-site weaving intentionally skips.</summary>
    /// <returns>The value returned by the struct instance call.</returns>
    public int RunStructInstanceCall()
    {
        var target = new CallSiteStructCalleeTarget();
        return target.Increment(1);
    }
}
