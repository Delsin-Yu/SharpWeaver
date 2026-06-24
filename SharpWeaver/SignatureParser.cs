namespace SharpWeaver;

/// <summary>
/// Parses a target signature string from an attribute into a structured representation.
/// Grammar: <c>FullTypeName.MethodName(ParamTypes...)</c>.
/// </summary>
public static class SignatureParser
{
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.Ordinal)
    {
        ["void"] = "System.Void",
        ["bool"] = "System.Boolean",
        ["byte"] = "System.Byte",
        ["sbyte"] = "System.SByte",
        ["char"] = "System.Char",
        ["short"] = "System.Int16",
        ["ushort"] = "System.UInt16",
        ["int"] = "System.Int32",
        ["uint"] = "System.UInt32",
        ["long"] = "System.Int64",
        ["ulong"] = "System.UInt64",
        ["float"] = "System.Single",
        ["double"] = "System.Double",
        ["decimal"] = "System.Decimal",
        ["string"] = "System.String",
        ["object"] = "System.Object",
    };

    /// <summary>Attempts to parse a target method signature.</summary>
    /// <param name="signature">Raw signature string.</param>
    /// <param name="parsed">Parsed result.</param>
    /// <param name="error">Human-readable error message on failure.</param>
    /// <returns>Returns <see langword="true"/> on successful parsing.</returns>
    public static bool TryParse(string signature, out ParsedSignature? parsed, out string? error)
    {
        parsed = null;
        error = null;

        if (string.IsNullOrWhiteSpace(signature))
        {
            error = "Target signature is empty.";
            return false;
        }

        var openParen = signature.LastIndexOf('(');
        var closeParen = signature.LastIndexOf(')');
        if (openParen < 0 || closeParen != signature.Length - 1 || closeParen <= openParen)
        {
            error = $"Signature '{signature}' has invalid format: missing matching parentheses '(...)'.";
            return false;
        }

        var beforeParams = signature[..openParen];
        var lastDot = beforeParams.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= beforeParams.Length - 1)
        {
            error = $"Signature '{signature}' has invalid format: unable to separate type name and method name.";
            return false;
        }

        var typeFullName = beforeParams[..lastDot].Trim();
        var methodName = beforeParams[(lastDot + 1)..].Trim();
        if (typeFullName.Length == 0 || methodName.Length == 0)
        {
            error = $"Signature '{signature}' has invalid format: type name or method name is empty.";
            return false;
        }

        var paramSection = signature[(openParen + 1)..closeParen].Trim();
        var rawParams = SplitParameters(paramSection);
        var resolvedParams = new string[rawParams.Count];
        for (var i = 0; i < rawParams.Count; i++)
        {
            var rawParam = rawParams[i].Trim();
            if (rawParam.Length == 0)
            {
                error = $"Signature '{signature}' has invalid format: parameter {i + 1} type is empty.";
                return false;
            }

            resolvedParams[i] = ResolveTypeName(rawParam);
        }

        parsed = new ParsedSignature(signature, typeFullName, methodName, resolvedParams);
        return true;
    }

    /// <summary>Expands common aliases to CLR fully qualified type names.</summary>
    /// <param name="typeName">Original type name or alias.</param>
    /// <returns>Fully qualified type name.</returns>
    public static string ResolveTypeName(string typeName)
    {
        if (TypeAliases.TryGetValue(typeName, out var alias))
        {
            return alias;
        }

        return typeName;
    }

    /// <summary>Formats a Cecil type reference as a signature display string.</summary>
    /// <param name="typeFullName">Fully qualified type name.</param>
    /// <returns>Display name preferring aliases where possible.</returns>
    public static string FormatTypeNameForDisplay(string typeFullName)
    {
        foreach (var pair in TypeAliases)
        {
            if (pair.Value == typeFullName)
            {
                return pair.Key;
            }
        }

        return typeFullName;
    }

    private static List<string> SplitParameters(string paramSection)
    {
        if (paramSection.Length == 0)
        {
            return [];
        }

        var parts = paramSection.Split(',');
        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
