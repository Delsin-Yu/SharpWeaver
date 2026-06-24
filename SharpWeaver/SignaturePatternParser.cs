namespace SharpWeaver;

/// <summary>Parses the <c>TargetSignature</c> of <see cref="WeaveAttribute"/> into an exact or wildcard pattern.</summary>
public static class SignaturePatternParser
{
    /// <summary>Attempts to parse a target signature pattern.</summary>
    /// <param name="rawSignature">Raw string from the attribute.</param>
    /// <param name="pattern">Parsed result.</param>
    /// <param name="error">Human-readable error on failure.</param>
    /// <returns>Returns <see langword="true"/> on successful parsing.</returns>
    public static bool TryParse(string rawSignature, out SignaturePattern? pattern, out string? error)
    {
        pattern = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawSignature))
        {
            error = "Target signature is empty.";
            return false;
        }

        if (rawSignature.StartsWith('^'))
        {
            error =
                $"Target signature '{rawSignature}' uses the removed '^' regex prefix; use wildcard syntax (* / **) instead.";
            return false;
        }

        if (rawSignature.Contains('*'))
        {
            if (!WildcardSignatureParser.TryParse(rawSignature, out var wildcard, out var wildcardError))
            {
                error = wildcardError;
                return false;
            }

            pattern = SignaturePattern.Wildcard(wildcard!);
            return true;
        }

        if (!SignatureParser.TryParse(rawSignature, out var parsed, out var parseError))
        {
            error = parseError;
            return false;
        }

        pattern = SignaturePattern.Exact(parsed!);
        return true;
    }
}
