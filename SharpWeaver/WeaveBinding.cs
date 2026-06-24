using Mono.Cecil;

namespace SharpWeaver;

/// <summary>ILWeaving weave binding for a single target signature: weave method list and override candidates to be woven.</summary>
public sealed class WeaveBinding
{
    /// <summary>Creates a weave binding.</summary>
    /// <param name="targetSignature">Target signature string.</param>
    /// <param name="targetMethod">Resolved target method.</param>
    /// <param name="weaves">List of weave methods (multiple allowed for the same target, applied in discovery order).</param>
    /// <param name="overrideMethods">Matching override candidates in the target assembly.</param>
    public WeaveBinding(
        string targetSignature,
        MethodDefinition targetMethod,
        IReadOnlyList<WeaveInfo> weaves,
        IReadOnlyList<MethodDefinition> overrideMethods)
    {
        TargetSignature = targetSignature;
        TargetMethod = targetMethod;
        Weaves = weaves;
        OverrideMethods = overrideMethods;
    }

    /// <summary>Target signature string.</summary>
    public string TargetSignature { get; }

    /// <summary>Resolved target method (typically from a reference assembly or base class in the target assembly).</summary>
    public MethodDefinition TargetMethod { get; }

    /// <summary>List of weave methods (multiple allowed for the same target, applied in discovery order).</summary>
    public IReadOnlyList<WeaveInfo> Weaves { get; }

    /// <summary>Matching override methods in the target assembly.</summary>
    public IReadOnlyList<MethodDefinition> OverrideMethods { get; }
}
