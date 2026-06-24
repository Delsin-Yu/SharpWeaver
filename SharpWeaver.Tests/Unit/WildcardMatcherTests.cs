using SharpWeaver;
using Mono.Cecil;
using Xunit;

namespace SharpWeaver.Tests;

/// <summary>Segment and parameter list wildcard matching tests.</summary>
public class WildcardMatcherTests
{
    /// <summary>Single <c>*</c> namespace segment should match exactly one segment.</summary>
    [Fact]
    public void SegmentMatcher_star_matches_one_namespace_segment()
    {
        var pattern = new[] { SegmentPattern.Exact("AkisFarm"), SegmentPattern.AnySingle };
        Assert.True(SegmentMatcher.MatchSequence(pattern, ["AkisFarm", "Services"]));
        Assert.False(SegmentMatcher.MatchSequence(pattern, ["AkisFarm"]));
    }

    /// <summary>Single <c>**</c> namespace segment should match zero or more segments.</summary>
    [Fact]
    public void SegmentMatcher_double_star_matches_zero_or_more_namespace_segments()
    {
        var pattern = new[] { SegmentPattern.Exact("AkisFarm"), SegmentPattern.Exact("Services"), SegmentPattern.ZeroOrMore };
        Assert.True(SegmentMatcher.MatchSequence(pattern, ["AkisFarm", "Services"]));
        Assert.True(SegmentMatcher.MatchSequence(pattern, ["AkisFarm", "Services", "PlayerShop"]));
        Assert.False(SegmentMatcher.MatchSequence(pattern, ["AkisFarm", "Utils"]));
    }

    /// <summary>Character-level glob <c>*base</c> should match <c>Database</c>.</summary>
    [Fact]
    public void SegmentMatcher_glob_matches_suffix()
    {
        Assert.True(SegmentMatcher.MatchName(SegmentPattern.Glob("*base"), "Database"));
        Assert.False(SegmentMatcher.MatchName(SegmentPattern.Glob("*base"), "Data"));
    }

    /// <summary>Character-level glob <c>*Service*</c> should match type names.</summary>
    [Fact]
    public void SegmentMatcher_glob_matches_contains()
    {
        Assert.True(SegmentMatcher.MatchName(SegmentPattern.Glob("*Service*"), "PlaceableServiceClient"));
        Assert.True(SegmentMatcher.MatchName(SegmentPattern.Glob("*String"), "ToString"));
    }

    /// <summary><c>double,*,int</c> should match exactly one parameter between double and int.</summary>
    [Fact]
    public void ParameterListMatcher_star_matches_one_parameter()
    {
        var pattern = new[] { ParameterPattern.Exact("double"), ParameterPattern.AnySingle, ParameterPattern.Exact("int") };
        Assert.True(ParameterListMatcher.Match(pattern, ["double", "float", "int"]));
        Assert.False(ParameterListMatcher.Match(pattern, ["double", "int"]));
    }

    /// <summary><c>double,**,int</c> should match zero or more parameters between double and int.</summary>
    [Fact]
    public void ParameterListMatcher_double_star_matches_zero_or_more_parameters()
    {
        var pattern = new[] { ParameterPattern.Exact("double"), ParameterPattern.ZeroOrMore, ParameterPattern.Exact("int") };
        Assert.True(ParameterListMatcher.Match(pattern, ["double", "int"]));
        Assert.True(ParameterListMatcher.Match(pattern, ["double", "float", "string", "int"]));
        Assert.False(ParameterListMatcher.Match(pattern, ["double", "float"]));
    }

    /// <summary>Single <c>(**)</c> should match any parameter list.</summary>
    [Fact]
    public void ParameterListMatcher_lone_double_star_matches_all_overloads()
    {
        var pattern = new[] { ParameterPattern.ZeroOrMore };
        Assert.True(ParameterListMatcher.Match(pattern, []));
        Assert.True(ParameterListMatcher.Match(pattern, ["double"]));
        Assert.True(ParameterListMatcher.Match(pattern, ["double", "int"]));
    }

    /// <summary><c>**.*._OnPanelOpen()</c> should match nested namespace types.</summary>
    [Fact]
    public void WildcardSignatureMatcher_matches_panel_open_pattern()
    {
        Assert.True(WildcardSignatureParser.TryParse("**.*._OnPanelOpen()", out var parsed, out var error), error);
        var candidateNs = new[] { "SharpWeaver", "TestFixtures", "Fake" };
        Assert.True(SegmentMatcher.MatchSequence(parsed!.NamespaceSegments, candidateNs));
        Assert.True(SegmentMatcher.MatchName(parsed.TypeName, "RegexPanelTarget"));
        Assert.True(SegmentMatcher.MatchName(parsed.MethodName, "_OnPanelOpen"));
        Assert.True(ParameterListMatcher.Match(parsed.Parameters, []));
    }

    /// <summary><c>AkisFarm.Services.**.*.*(**)</c> should match service types under direct child namespaces.</summary>
    [Fact]
    public void WildcardSignatureMatcher_services_pattern_matches_direct_child_type()
    {
        Assert.True(
            WildcardSignatureParser.TryParse("AkisFarm.Services.**.*.*(**)", out var parsed, out var error),
            error);
        var candidateNs = new[] { "AkisFarm", "Services" };
        Assert.True(SegmentMatcher.MatchSequence(parsed!.NamespaceSegments, candidateNs));
        Assert.True(SegmentMatcher.MatchName(parsed.TypeName, "PlaceableServiceClient"));
        Assert.True(SegmentMatcher.MatchName(parsed.MethodName, "Run"));
        Assert.True(ParameterListMatcher.Match(parsed.Parameters, ["int"]));
    }

    /// <summary>Nested types should be excludable via the outer type path in namespace wildcards.</summary>
    [Fact]
    public void WildcardSignatureMatcher_nested_type_uses_outer_type_path_as_namespace()
    {
        Assert.True(
            WildcardSignatureParser.TryParse("AkisFarm.Utils.Profiling.**.*.Dispose()", out var parsed, out var error),
            error);

        var outer = new TypeDefinition(
            "AkisFarm.Utils.Profiling",
            "TracyProfiler",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var nested = new TypeDefinition(
            string.Empty,
            "Zone",
            TypeAttributes.NestedPublic | TypeAttributes.Sealed);
        outer.NestedTypes.Add(nested);
        var dispose = new MethodDefinition(
            "Dispose",
            MethodAttributes.Public,
            new TypeReference("System", "Void", null, null));
        nested.Methods.Add(dispose);

        Assert.True(WildcardSignatureMatcher.IsMatch(parsed!, dispose));
    }

    /// <summary>Exact exclusion signatures should be able to match candidate methods by name.</summary>
    [Fact]
    public void WeaveExclusionMatcher_exact_signature_excludes_matching_method()
    {
        Assert.True(
            SignaturePatternParser.TryParse("AkisFarm.UI.Controls.ItemDetail.ItemDetailScrollBar._Process(double)", out var pattern, out var error),
            error);

        var declaringType = new TypeDefinition(
            "AkisFarm.UI.Controls.ItemDetail",
            "ItemDetailScrollBar",
            TypeAttributes.Public);
        var process = new MethodDefinition(
            "_Process",
            MethodAttributes.Public,
            new TypeReference("System", "Void", null, null));
        process.Parameters.Add(new ParameterDefinition(new TypeReference("System", "Double", null, null)));
        declaringType.Methods.Add(process);

        var weave = new WeaveInfo(
            "Godot.Node._Process(double)",
            SignaturePattern.Exact(new ParsedSignature("Godot.Node._Process(double)", "Godot.Node", "_Process", ["System.Double"])),
            0,
            process,
            "PatchType",
            0,
            [pattern!]);

        Assert.True(WeaveExclusionMatcher.IsExcluded(weave, process));
    }
}
