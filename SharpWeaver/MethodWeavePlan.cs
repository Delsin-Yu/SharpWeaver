using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver;

/// <summary>A matching call instruction inside a caller method, plus call-site weaves to apply to it.</summary>
public sealed class CallSiteWeaveMatch
{
    /// <summary>Creates a call-site weave match.</summary>
    /// <param name="callInstruction">The original <c>call</c> or <c>callvirt</c> instruction.</param>
    /// <param name="calledMethod">The called method reference as emitted in the caller IL.</param>
    /// <param name="resolvedCalledMethod">Resolved called method definition.</param>
    /// <param name="weaves">CallSite-code weaves for this call site.</param>
    public CallSiteWeaveMatch(
        Instruction callInstruction,
        MethodReference calledMethod,
        MethodDefinition resolvedCalledMethod,
        List<WeaveInfo> weaves)
    {
        CallInstruction = callInstruction;
        CalledMethod = calledMethod;
        ResolvedCalledMethod = resolvedCalledMethod;
        Weaves = weaves;
    }

    /// <summary>The original <c>call</c> or <c>callvirt</c> instruction.</summary>
    public Instruction CallInstruction { get; }

    /// <summary>The called method reference as emitted in the caller IL.</summary>
    public MethodReference CalledMethod { get; }

    /// <summary>Resolved called method definition.</summary>
    public MethodDefinition ResolvedCalledMethod { get; }

    /// <summary>CallSite-code weaves for this call site.</summary>
    public List<WeaveInfo> Weaves { get; }
}

/// <summary>A single method to be woven along with its priority-sorted weave list.</summary>
public sealed class MethodWeavePlan
{
    /// <summary>Creates a method weave plan.</summary>
    /// <param name="method">Method to be woven (override, wildcard-matched direct method, or async <c>MoveNext</c>).</param>
    /// <param name="weaves">Weave list sorted by ascending priority.</param>
    /// <param name="resolvedTargetMethod">Resolved target method for exact mode; the user-visible method being woven for wildcard mode.</param>
    /// <param name="outerMethod">User-visible method used for metadata capture and validation; the outer async method for async, otherwise same as <paramref name="method"/>.</param>
    /// <param name="callSiteMatches">Call-site call-site matches in this method.</param>
    public MethodWeavePlan(
        MethodDefinition method,
        IReadOnlyList<WeaveInfo> weaves,
        MethodDefinition resolvedTargetMethod,
        MethodDefinition? outerMethod = null,
        IReadOnlyList<CallSiteWeaveMatch>? callSiteMatches = null)
    {
        Method = method;
        Weaves = weaves;
        ResolvedTargetMethod = resolvedTargetMethod;
        OuterMethod = outerMethod ?? method;
        CallSiteMatches = callSiteMatches ?? [];
    }

    /// <summary>Method to be woven (the method body that actually receives spliced IL).</summary>
    public MethodDefinition Method { get; }

    /// <summary>Weave list sorted by ascending priority.</summary>
    public IReadOnlyList<WeaveInfo> Weaves { get; }

    /// <summary>Resolved target method for exact mode.</summary>
    public MethodDefinition ResolvedTargetMethod { get; }

    /// <summary>User-visible outer method used for metadata capture and async validation.</summary>
    public MethodDefinition OuterMethod { get; }

    /// <summary>Call-site call-site matches inside this method.</summary>
    public IReadOnlyList<CallSiteWeaveMatch> CallSiteMatches { get; }
}

/// <summary>Planning result from <see cref="MethodWeavePlanner"/>.</summary>
public sealed class MethodWeavePlannerResult
{
    /// <summary>Creates a planning result.</summary>
    /// <param name="plans">List of method weave plans.</param>
    /// <param name="errors">List of error messages.</param>
    public MethodWeavePlannerResult(IReadOnlyList<MethodWeavePlan> plans, IReadOnlyList<string> errors)
    {
        Plans = plans;
        Errors = errors;
    }

    /// <summary>List of method weave plans.</summary>
    public IReadOnlyList<MethodWeavePlan> Plans { get; }

    /// <summary>List of error messages.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Whether all operations succeeded (no errors).</summary>
    public bool Success => Errors.Count == 0;
}
