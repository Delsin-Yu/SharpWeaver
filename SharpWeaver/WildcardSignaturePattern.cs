namespace SharpWeaver;

/// <summary>Parsed wildcard target method signature pattern.</summary>
public sealed class WildcardSignaturePattern
{
    /// <summary>Creates a wildcard signature pattern.</summary>
    /// <param name="raw">Raw signature string.</param>
    /// <param name="namespaceSegments">Namespace segment pattern list.</param>
    /// <param name="typeName">Type short name pattern.</param>
    /// <param name="methodName">Method name pattern.</param>
    /// <param name="parameters">Parameter list pattern.</param>
    public WildcardSignaturePattern(
        string raw,
        IReadOnlyList<SegmentPattern> namespaceSegments,
        SegmentPattern typeName,
        SegmentPattern methodName,
        IReadOnlyList<ParameterPattern> parameters)
    {
        Raw = raw;
        NamespaceSegments = namespaceSegments;
        TypeName = typeName;
        MethodName = methodName;
        Parameters = parameters;
    }

    /// <summary>Raw signature string.</summary>
    public string Raw { get; }

    /// <summary>Namespace segment patterns (dot-separated).</summary>
    public IReadOnlyList<SegmentPattern> NamespaceSegments { get; }

    /// <summary>Type short name pattern.</summary>
    public SegmentPattern TypeName { get; }

    /// <summary>Method name pattern.</summary>
    public SegmentPattern MethodName { get; }

    /// <summary>Parameter list pattern.</summary>
    public IReadOnlyList<ParameterPattern> Parameters { get; }
}
