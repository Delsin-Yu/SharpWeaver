using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Information about an ILWeaving / AsyncILWeaving weave method discovered in the target assembly.</summary>
public sealed class WeaveInfo
{
    private int _discoveryOrder;

    /// <summary>Creates weave method information.</summary>
    /// <param name="targetSignature">Target signature string.</param>
    /// <param name="pattern">Parsed signature pattern.</param>
    /// <param name="priority">Weave priority.</param>
    /// <param name="weaveMethod">Weave method definition.</param>
    /// <param name="declaringType">Weave method declaring type fully qualified name.</param>
    /// <param name="discoveryOrder">Scan discovery order (used for stable sorting when priorities are equal).</param>
    /// <param name="excludePatterns">Wildcard target exclusion patterns for this weave method.</param>
    /// <param name="isAsync">Whether this is an <see cref="AsyncWeaveAttribute"/> async weave.</param>
    /// <param name="genericWeave">Whether matching open generic targets is allowed.</param>
    /// <param name="excludeAsyncLikeReturn">Whether sync weaving should exclude async-like return types.</param>
    public WeaveInfo(
        string targetSignature,
        SignaturePattern pattern,
        int priority,
        MethodDefinition weaveMethod,
        string declaringType,
        int discoveryOrder,
        IReadOnlyList<SignaturePattern>? excludePatterns = null,
        bool isAsync = false,
        bool genericWeave = false,
        bool excludeAsyncLikeReturn = false)
    {
        TargetSignature = targetSignature;
        Pattern = pattern;
        Priority = priority;
        WeaveMethod = weaveMethod;
        DeclaringType = declaringType;
        _discoveryOrder = discoveryOrder;
        ExcludePatterns = excludePatterns ?? [];
        IsAsync = isAsync;
        GenericWeave = genericWeave;
        ExcludeAsyncLikeReturn = excludeAsyncLikeReturn;
    }

    /// <summary>Target signature string (raw).</summary>
    public string TargetSignature { get; }

    /// <summary>Parsed signature pattern.</summary>
    public SignaturePattern Pattern { get; }

    /// <summary>Weave priority (ascending order of application).</summary>
    public int Priority { get; }

    /// <summary>Weave method definition (containing <c>WeaveTemplate.OriginalBody()</c> marker call).</summary>
    public MethodDefinition WeaveMethod { get; }

    /// <summary>Weave method declaring type fully qualified name.</summary>
    public string DeclaringType { get; }

    /// <summary>Scan discovery order.</summary>
    public int DiscoveryOrder => _discoveryOrder;

    /// <summary>Whether this is an async weave (target is state machine <c>MoveNext</c>).</summary>
    public bool IsAsync { get; }

    /// <summary>Whether to match methods or declaring types with open generic parameters.</summary>
    public bool GenericWeave { get; }

    /// <summary>Whether sync weaving should exclude methods returning async-like types.</summary>
    public bool ExcludeAsyncLikeReturn { get; }

    /// <summary>Signature patterns to exclude from the target set during wildcard matching.</summary>
    public IReadOnlyList<SignaturePattern> ExcludePatterns { get; }

    /// <summary>Weave method fully qualified name (type.method).</summary>
    public string WeaveMethodDisplayName => $"{DeclaringType}.{WeaveMethod.Name}";
}
