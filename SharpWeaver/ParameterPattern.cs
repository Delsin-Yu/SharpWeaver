namespace SharpWeaver;

/// <summary>Parameter list slot wildcard pattern kinds.</summary>
public enum ParameterPatternKind
{
    /// <summary>Exact type literal.</summary>
    Exact,

    /// <summary>Single <c>*</c>: exactly one parameter of any type.</summary>
    AnySingle,

    /// <summary>Single <c>**</c>: zero or more parameters.</summary>
    ZeroOrMore,
}

/// <summary>A slot pattern in a parameter list.</summary>
public readonly struct ParameterPattern
{
    /// <summary>Creates a parameter pattern.</summary>
    /// <param name="kind">Pattern kind.</param>
    /// <param name="displayTypeName">Display type name (only used for exact mode).</param>
    public ParameterPattern(ParameterPatternKind kind, string displayTypeName = "")
    {
        Kind = kind;
        DisplayTypeName = displayTypeName;
    }

    /// <summary>Pattern kind.</summary>
    public ParameterPatternKind Kind { get; }

    /// <summary>Display type name (aliases expanded to display form).</summary>
    public string DisplayTypeName { get; }

    /// <summary>Exact parameter type.</summary>
    /// <param name="displayTypeName">Display type name.</param>
    /// <returns>Exact parameter pattern.</returns>
    public static ParameterPattern Exact(string displayTypeName) =>
        new(ParameterPatternKind.Exact, displayTypeName);

    /// <summary>Single asterisk slot.</summary>
    public static ParameterPattern AnySingle { get; } = new(ParameterPatternKind.AnySingle);

    /// <summary>Single double-asterisk slot.</summary>
    public static ParameterPattern ZeroOrMore { get; } = new(ParameterPatternKind.ZeroOrMore);
}
