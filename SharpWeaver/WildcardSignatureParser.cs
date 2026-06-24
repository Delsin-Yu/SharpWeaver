namespace SharpWeaver;

/// <summary>Parses a wildcard target signature string into a <see cref="WildcardSignaturePattern"/>.</summary>
public static class WildcardSignatureParser
{
    /// <summary>Attempts to parse a wildcard signature.</summary>
    /// <param name="signature">Raw signature string.</param>
    /// <param name="pattern">Parsed result.</param>
    /// <param name="error">Human-readable error on failure.</param>
    /// <returns>Returns <see langword="true"/> on successful parsing.</returns>
    public static bool TryParse(string signature, out WildcardSignaturePattern? pattern, out string? error)
    {
        pattern = null;
        error = null;

        if (string.IsNullOrWhiteSpace(signature))
        {
            error = "Target signature is empty.";
            return false;
        }

        if (!TrySplitSignature(signature, out var namespacePath, out var typeSegment, out var methodSegment, out var paramSection, out error))
        {
            return false;
        }

        var namespaceSegments = new List<SegmentPattern>();
        if (namespacePath.Length > 0)
        {
            foreach (var segmentText in namespacePath.Split('.'))
            {
                if (segmentText.Length == 0)
                {
                    error = $"签名 '{signature}' 格式无效：命名空间路径含空段（'..'）。";
                    return false;
                }

                if (!TryParseNamespaceSegment(segmentText, out var segmentPattern, out error))
                {
                    return false;
                }

                namespaceSegments.Add(segmentPattern);
            }
        }

        if (!TryParseNameSegment(typeSegment, allowZeroOrMore: false, out var typePattern, out error))
        {
            return false;
        }

        if (!TryParseNameSegment(methodSegment, allowZeroOrMore: false, out var methodPattern, out error))
        {
            return false;
        }

        var parameterPatterns = new List<ParameterPattern>();
        foreach (var paramToken in SplitParameterTokens(paramSection))
        {
            if (!TryParseParameterToken(paramToken, out var paramPattern, out error))
            {
                return false;
            }

            parameterPatterns.Add(paramPattern);
        }

        pattern = new WildcardSignaturePattern(
            signature,
            namespaceSegments,
            typePattern,
            methodPattern,
            parameterPatterns);
        return true;
    }

    private static bool TrySplitSignature(
        string signature,
        out string namespacePath,
        out string typeSegment,
        out string methodSegment,
        out string paramSection,
        out string? error)
    {
        namespacePath = string.Empty;
        typeSegment = string.Empty;
        methodSegment = string.Empty;
        paramSection = string.Empty;
        error = null;

        var openParen = signature.LastIndexOf('(');
        var closeParen = signature.LastIndexOf(')');
        if (openParen < 0 || closeParen != signature.Length - 1 || closeParen <= openParen)
        {
            error = $"签名 '{signature}' 格式无效：缺少匹配的括号 '(...)'。";
            return false;
        }

        var beforeParams = signature[..openParen];
        var lastDot = beforeParams.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= beforeParams.Length - 1)
        {
            error = $"签名 '{signature}' 格式无效：无法分离类型名与方法名。";
            return false;
        }

        methodSegment = beforeParams[(lastDot + 1)..].Trim();
        var beforeMethod = beforeParams[..lastDot];
        var typeDot = beforeMethod.LastIndexOf('.');
        if (typeDot < 0)
        {
            typeSegment = beforeMethod.Trim();
            namespacePath = string.Empty;
        }
        else
        {
            typeSegment = beforeMethod[(typeDot + 1)..].Trim();
            namespacePath = beforeMethod[..typeDot].Trim();
        }

        if (typeSegment.Length == 0 || methodSegment.Length == 0)
        {
            error = $"签名 '{signature}' 格式无效：类型名或方法名为空。";
            return false;
        }

        paramSection = signature[(openParen + 1)..closeParen].Trim();
        return true;
    }

    private static List<string> SplitParameterTokens(string paramSection)
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

    private static bool TryParseNamespaceSegment(string text, out SegmentPattern pattern, out string? error) =>
        TryParseNameSegment(text, allowZeroOrMore: true, out pattern, out error);

    private static bool TryParseNameSegment(
        string text,
        bool allowZeroOrMore,
        out SegmentPattern pattern,
        out string? error)
    {
        pattern = default;
        error = null;

        if (text == "**")
        {
            if (!allowZeroOrMore)
            {
                error = $"签名段 '{text}' 无效：类型名与方法名不得使用 '**'。";
                return false;
            }

            pattern = SegmentPattern.ZeroOrMore;
            return true;
        }

        if (text == "*")
        {
            pattern = SegmentPattern.AnySingle;
            return true;
        }

        if (text.Contains('*'))
        {
            if (text.Contains("**", StringComparison.Ordinal))
            {
                error = $"签名段 '{text}' 无效：glob 段不得包含 '**'。";
                return false;
            }

            pattern = SegmentPattern.Glob(text);
            return true;
        }

        pattern = SegmentPattern.Exact(text);
        return true;
    }

    private static bool TryParseParameterToken(string text, out ParameterPattern pattern, out string? error)
    {
        pattern = default;
        error = null;

        if (text == "**")
        {
            pattern = ParameterPattern.ZeroOrMore;
            return true;
        }

        if (text == "*")
        {
            pattern = ParameterPattern.AnySingle;
            return true;
        }

        if (text.Contains('*'))
        {
            error = $"参数类型槽位 '{text}' 无效：参数列表仅支持字面量、'*' 或 '**'。";
            return false;
        }

        var displayName = SignatureParser.FormatTypeNameForDisplay(SignatureParser.ResolveTypeName(text));
        pattern = ParameterPattern.Exact(displayName);
        return true;
    }
}
