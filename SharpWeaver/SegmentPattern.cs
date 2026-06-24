namespace SharpWeaver;

/// <summary>Wildcard segment pattern kinds.</summary>
public enum SegmentPatternKind
{
    /// <summary>Exact literal match.</summary>
    Exact,

    /// <summary>Single <c>*</c>: exactly one arbitrary segment or name.</summary>
    AnySingle,

    /// <summary>Single <c>**</c>: zero or more segments (namespace path only).</summary>
    ZeroOrMore,

    /// <summary>Character-level glob containing <c>*</c> (e.g., <c>*base</c>, <c>*Service*</c>).</summary>
    Glob,
}

/// <summary>Wildcard pattern for namespace segments, type names, or method names.</summary>
public readonly struct SegmentPattern
{
    /// <summary>Creates a segment pattern.</summary>
    /// <param name="kind">Pattern kind.</param>
    /// <param name="literal">Literal or glob text (only used for <see cref="SegmentPatternKind.Exact"/> / <see cref="SegmentPatternKind.Glob"/>).</param>
    public SegmentPattern(SegmentPatternKind kind, string literal = "")
    {
        Kind = kind;
        Literal = literal;
    }

    /// <summary>Pattern kind.</summary>
    public SegmentPatternKind Kind { get; }

    /// <summary>Literal or glob text.</summary>
    public string Literal { get; }

    /// <summary>Exact literal segment.</summary>
    /// <param name="text">Segment text.</param>
    /// <returns>Exact segment pattern.</returns>
    public static SegmentPattern Exact(string text) => new(SegmentPatternKind.Exact, text);

    /// <summary>Single asterisk segment.</summary>
    public static SegmentPattern AnySingle { get; } = new(SegmentPatternKind.AnySingle);

    /// <summary>Single double-asterisk segment (zero or more namespace segments).</summary>
    public static SegmentPattern ZeroOrMore { get; } = new(SegmentPatternKind.ZeroOrMore);

    /// <summary>Character-level glob segment.</summary>
    /// <param name="glob">Glob text.</param>
    /// <returns>Glob segment pattern.</returns>
    public static SegmentPattern Glob(string glob) => new(SegmentPatternKind.Glob, glob);
}
