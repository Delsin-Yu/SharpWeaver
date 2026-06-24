namespace SharpWeaver;

/// <summary>Character-level glob matching (<c>*</c> matches zero or more arbitrary characters).</summary>
public static class GlobMatcher
{
    /// <summary>Determines whether the glob matches the text.</summary>
    /// <param name="glob">Glob pattern containing <c>*</c>.</param>
    /// <param name="text">Candidate text.</param>
    /// <returns>Returns <see langword="true"/> if it matches.</returns>
    public static bool IsMatch(string glob, string text) => IsMatch(glob.AsSpan(), text.AsSpan());

    /// <summary>Determines whether the glob matches the text.</summary>
    /// <param name="glob">Glob pattern containing <c>*</c>.</param>
    /// <param name="text">Candidate text.</param>
    /// <returns>Returns <see langword="true"/> if it matches.</returns>
    public static bool IsMatch(ReadOnlySpan<char> glob, ReadOnlySpan<char> text) =>
        IsMatchRecursive(glob, text);

    private static bool IsMatchRecursive(ReadOnlySpan<char> glob, ReadOnlySpan<char> text)
    {
        while (glob.Length > 0)
        {
            if (glob[0] == '*')
            {
                glob = glob[1..];
                if (glob.Length == 0)
                {
                    return true;
                }

                for (var textIndex = 0; textIndex <= text.Length; textIndex++)
                {
                    if (IsMatchRecursive(glob, text[textIndex..]))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (text.Length == 0 || glob[0] != text[0])
            {
                return false;
            }

            glob = glob[1..];
            text = text[1..];
        }

        return text.Length == 0;
    }
}
