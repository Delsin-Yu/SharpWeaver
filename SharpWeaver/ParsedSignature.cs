namespace SharpWeaver;

/// <summary>Parsed CLR target method signature.</summary>
public sealed class ParsedSignature
{
    /// <summary>Creates a parsed signature.</summary>
    /// <param name="rawSignature">Raw signature string.</param>
    /// <param name="typeFullName">Declaring type fully qualified name.</param>
    /// <param name="methodName">Method name.</param>
    /// <param name="parameterTypeNames">List of parameter type fully qualified names with aliases expanded.</param>
    public ParsedSignature(
        string rawSignature,
        string typeFullName,
        string methodName,
        IReadOnlyList<string> parameterTypeNames)
    {
        RawSignature = rawSignature;
        TypeFullName = typeFullName;
        MethodName = methodName;
        ParameterTypeNames = parameterTypeNames;
    }

    /// <summary>Raw signature string.</summary>
    public string RawSignature { get; }

    /// <summary>Declaring type fully qualified name, e.g., <c>Godot.Node</c>.</summary>
    public string TypeFullName { get; }

    /// <summary>Method name.</summary>
    public string MethodName { get; }

    /// <summary>List of parameter type fully qualified names (aliases expanded).</summary>
    public IReadOnlyList<string> ParameterTypeNames { get; }
}
