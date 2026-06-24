using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Determines whether a weave target matches a <see cref="WeaveExcludeAttribute"/> exclusion pattern.</summary>
public static class WeaveExclusionMatcher
{
    /// <summary>Determines whether the specified method is excluded by any exclusion pattern on the weave info.</summary>
    /// <param name="weave">Weave information.</param>
    /// <param name="method">Candidate target method.</param>
    /// <returns>Returns <see langword="true"/> if the method matches an exclusion pattern.</returns>
    public static bool IsExcluded(WeaveInfo weave, MethodDefinition method)
    {
        foreach (var pattern in weave.ExcludePatterns)
        {
            switch (pattern)
            {
                case ExactSignaturePattern exact when IsExactMatch(exact.Parsed, method):
                case WildcardSignaturePatternWrapper wildcard when WildcardSignatureMatcher.IsMatch(wildcard.Parsed, method):
                    return true;
            }
        }

        return false;
    }

    private static bool IsExactMatch(ParsedSignature signature, MethodDefinition method)
    {
        if (method.DeclaringType.FullName != signature.TypeFullName
            || method.Name != signature.MethodName
            || method.Parameters.Count != signature.ParameterTypeNames.Count)
        {
            return false;
        }

        for (var i = 0; i < method.Parameters.Count; i++)
        {
            if (method.Parameters[i].ParameterType.FullName != signature.ParameterTypeNames[i])
            {
                return false;
            }
        }

        return true;
    }
}
