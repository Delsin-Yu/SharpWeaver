namespace SharpWeaver;

/// <summary>Wildcard matching for namespace segment sequences and individual names.</summary>
public static class SegmentMatcher
{
    /// <summary>Splits a namespace string into dot-separated segments.</summary>
    /// <param name="namespace">Namespace; returns empty list when null or empty.</param>
    /// <returns>List of namespace segments.</returns>
    public static IReadOnlyList<string> SplitNamespace(string? @namespace)
    {
        if (string.IsNullOrEmpty(@namespace))
        {
            return [];
        }

        return @namespace.Split('.');
    }

    /// <summary>Matches a namespace segment sequence.</summary>
    /// <param name="pattern">Segment pattern sequence.</param>
    /// <param name="candidateSegments">Candidate namespace segments.</param>
    /// <returns>Returns <see langword="true"/> if it matches.</returns>
    public static bool MatchSequence(IReadOnlyList<SegmentPattern> pattern, IReadOnlyList<string> candidateSegments) =>
        MatchSequence(pattern, 0, candidateSegments, 0);

    /// <summary>Matches a single type name or method name.</summary>
    /// <param name="pattern">Name pattern (must not be <see cref="SegmentPatternKind.ZeroOrMore"/>).</param>
    /// <param name="candidateName">Candidate name.</param>
    /// <returns>Returns <see langword="true"/> if it matches.</returns>
    public static bool MatchName(SegmentPattern pattern, string candidateName)
    {
        return pattern.Kind switch
        {
            SegmentPatternKind.Exact => candidateName == pattern.Literal,
            SegmentPatternKind.AnySingle => candidateName.Length > 0,
            SegmentPatternKind.Glob => GlobMatcher.IsMatch(pattern.Literal, candidateName),
            SegmentPatternKind.ZeroOrMore => false,
            _ => false,
        };
    }

    private static bool MatchSequence(
        IReadOnlyList<SegmentPattern> pattern,
        int patternIndex,
        IReadOnlyList<string> candidateSegments,
        int candidateIndex)
    {
        if (patternIndex == pattern.Count)
        {
            return candidateIndex == candidateSegments.Count;
        }

        var segmentPattern = pattern[patternIndex];
        switch (segmentPattern.Kind)
        {
            case SegmentPatternKind.Exact:
                if (candidateIndex >= candidateSegments.Count
                    || candidateSegments[candidateIndex] != segmentPattern.Literal)
                {
                    return false;
                }

                return MatchSequence(pattern, patternIndex + 1, candidateSegments, candidateIndex + 1);

            case SegmentPatternKind.AnySingle:
                if (candidateIndex >= candidateSegments.Count)
                {
                    return false;
                }

                return MatchSequence(pattern, patternIndex + 1, candidateSegments, candidateIndex + 1);

            case SegmentPatternKind.ZeroOrMore:
                for (var consume = 0; consume <= candidateSegments.Count - candidateIndex; consume++)
                {
                    if (MatchSequence(pattern, patternIndex + 1, candidateSegments, candidateIndex + consume))
                    {
                        return true;
                    }
                }

                return false;

            case SegmentPatternKind.Glob:
                if (candidateIndex >= candidateSegments.Count
                    || !GlobMatcher.IsMatch(segmentPattern.Literal, candidateSegments[candidateIndex]))
                {
                    return false;
                }

                return MatchSequence(pattern, patternIndex + 1, candidateSegments, candidateIndex + 1);

            default:
                return false;
        }
    }
}
