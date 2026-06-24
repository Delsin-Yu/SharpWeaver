namespace SharpWeaver;

/// <summary>ILWeaving target signature pattern: exact literal or wildcard.</summary>
public abstract class SignaturePattern
{
    /// <summary>Raw pattern string.</summary>
    public abstract string Raw { get; }

    /// <summary>Whether this is an exact literal pattern.</summary>
    public bool IsExact => this is ExactSignaturePattern;

    /// <summary>Whether this is a wildcard pattern.</summary>
    public bool IsWildcard => this is WildcardSignaturePatternWrapper;

    /// <summary>Creates an exact literal pattern.</summary>
    /// <param name="parsed">Parsed signature.</param>
    /// <returns>Exact pattern instance.</returns>
    public static SignaturePattern Exact(ParsedSignature parsed) => new ExactSignaturePattern(parsed);

    /// <summary>Creates a wildcard pattern.</summary>
    /// <param name="wildcard">Parsed wildcard signature.</param>
    /// <returns>Wildcard pattern instance.</returns>
    public static SignaturePattern Wildcard(WildcardSignaturePattern wildcard) =>
        new WildcardSignaturePatternWrapper(wildcard);
}

/// <summary>Exact CLR method signature pattern.</summary>
public sealed class ExactSignaturePattern : SignaturePattern
{
    /// <summary>Creates an exact pattern.</summary>
    /// <param name="parsed">Parsed signature.</param>
    public ExactSignaturePattern(ParsedSignature parsed)
    {
        Parsed = parsed;
    }

    /// <summary>Parsed signature.</summary>
    public ParsedSignature Parsed { get; }

    /// <inheritdoc />
    public override string Raw => Parsed.RawSignature;
}

/// <summary>Wildcard method signature pattern wrapper.</summary>
public sealed class WildcardSignaturePatternWrapper : SignaturePattern
{
    /// <summary>Creates a wildcard pattern wrapper.</summary>
    /// <param name="wildcard">Parsed wildcard signature.</param>
    public WildcardSignaturePatternWrapper(WildcardSignaturePattern wildcard)
    {
        Parsed = wildcard;
    }

    /// <summary>Parsed wildcard signature.</summary>
    public WildcardSignaturePattern Parsed { get; }

    /// <inheritdoc />
    public override string Raw => Parsed.Raw;
}
