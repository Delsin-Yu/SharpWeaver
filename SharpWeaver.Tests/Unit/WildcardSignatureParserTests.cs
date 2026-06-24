using SharpWeaver;
using Xunit;

namespace SharpWeaver.Tests;

/// <summary><see cref="WildcardSignatureParser"/> parsing tests.</summary>
public class WildcardSignatureParserTests
{
    /// <summary>Exact signatures without wildcards should go through <see cref="SignaturePatternParser"/>'s exact branch.</summary>
    [Fact]
    public void SignaturePatternParser_exact_signature_has_no_wildcard()
    {
        Assert.True(
            SignaturePatternParser.TryParse("Godot.Node._Process(double)", out var pattern, out var error),
            error);
        Assert.True(pattern!.IsExact);
    }

    /// <summary>The removed <c>^</c> regex prefix should be rejected.</summary>
    [Fact]
    public void SignaturePatternParser_rejects_caret_regex_prefix()
    {
        Assert.False(SignaturePatternParser.TryParse(@"^AkisFarm\.Services\..+", out _, out var error));
        Assert.Contains("removed", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'^'", error);
    }

    /// <summary>Wildcard signatures should parse to <see cref="WildcardSignaturePatternWrapper"/>.</summary>
    [Fact]
    public void SignaturePatternParser_parses_wildcard_signature()
    {
        Assert.True(
            SignaturePatternParser.TryParse("AkisFarm.Services.**.*.*(**)", out var pattern, out var error),
            error);
        Assert.True(pattern!.IsWildcard);
        var wildcard = ((WildcardSignaturePatternWrapper)pattern).Parsed;
        Assert.Equal(3, wildcard.NamespaceSegments.Count);
        Assert.Equal(SegmentPatternKind.Exact, wildcard.NamespaceSegments[0].Kind);
        Assert.Equal("AkisFarm", wildcard.NamespaceSegments[0].Literal);
        Assert.Equal(SegmentPatternKind.ZeroOrMore, wildcard.NamespaceSegments[2].Kind);
        Assert.Equal(SegmentPatternKind.AnySingle, wildcard.TypeName.Kind);
        Assert.Equal(SegmentPatternKind.AnySingle, wildcard.MethodName.Kind);
    }

    /// <summary><c>**</c> in type name should be rejected.</summary>
    [Fact]
    public void WildcardSignatureParser_rejects_double_star_in_type_name()
    {
        Assert.False(WildcardSignatureParser.TryParse("**.**.Method()", out _, out var error));
        Assert.Contains("**", error);
    }

    /// <summary>Empty namespace segments (<c>..</c>) should be rejected.</summary>
    [Fact]
    public void WildcardSignatureParser_rejects_empty_namespace_segment()
    {
        Assert.False(WildcardSignatureParser.TryParse("Name..Type.Method()", out _, out var error));
        Assert.Contains("空段", error);
    }

    /// <summary>Parameter slot globs should be rejected.</summary>
    [Fact]
    public void WildcardSignatureParser_rejects_glob_in_parameter_slot()
    {
        Assert.False(WildcardSignatureParser.TryParse("Type.Method(*String)", out _, out var error));
        Assert.Contains("参数", error);
    }

    /// <summary><c>***</c> type segment should be rejected.</summary>
    [Fact]
    public void WildcardSignatureParser_rejects_triple_star_type_segment()
    {
        Assert.False(WildcardSignatureParser.TryParse("***.Method()", out _, out var error));
        Assert.Contains("**", error);
    }
}
