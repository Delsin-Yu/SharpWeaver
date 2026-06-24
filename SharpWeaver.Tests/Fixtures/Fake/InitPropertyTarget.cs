namespace SharpWeaver.TestFixtures.Fake;

/// <summary>
/// Record struct with <c>init</c> accessor, used to verify <c>void modreq(IsExternalInit)</c> setter weaving.
/// </summary>
public record struct InitPropertyTarget
{
    /// <summary>Test value that can be init-set.</summary>
    public int Value { get; init; }
}
