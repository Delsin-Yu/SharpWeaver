namespace SharpWeaver;

/// <summary>Parameter list wildcard sequence matching.</summary>
public static class ParameterListMatcher
{
    /// <summary>Matches a parameter list pattern against candidate parameter type display names.</summary>
    /// <param name="pattern">Parameter pattern sequence.</param>
    /// <param name="candidateTypes">Candidate parameter type display names.</param>
    /// <returns>Returns <see langword="true"/> if it matches.</returns>
    public static bool Match(IReadOnlyList<ParameterPattern> pattern, IReadOnlyList<string> candidateTypes) =>
        Match(pattern, 0, candidateTypes, 0);

    private static bool Match(
        IReadOnlyList<ParameterPattern> pattern,
        int patternIndex,
        IReadOnlyList<string> candidateTypes,
        int candidateIndex)
    {
        if (patternIndex == pattern.Count)
        {
            return candidateIndex == candidateTypes.Count;
        }

        var slot = pattern[patternIndex];
        switch (slot.Kind)
        {
            case ParameterPatternKind.Exact:
                if (candidateIndex >= candidateTypes.Count
                    || candidateTypes[candidateIndex] != slot.DisplayTypeName)
                {
                    return false;
                }

                return Match(pattern, patternIndex + 1, candidateTypes, candidateIndex + 1);

            case ParameterPatternKind.AnySingle:
                if (candidateIndex >= candidateTypes.Count)
                {
                    return false;
                }

                return Match(pattern, patternIndex + 1, candidateTypes, candidateIndex + 1);

            case ParameterPatternKind.ZeroOrMore:
                for (var consume = 0; consume <= candidateTypes.Count - candidateIndex; consume++)
                {
                    if (Match(pattern, patternIndex + 1, candidateTypes, candidateIndex + consume))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }
}
