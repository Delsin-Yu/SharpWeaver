using SharpWeaver;
using Mono.Cecil;
using Xunit;

namespace SharpWeaver.Tests;

/// <summary><see cref="WeaveMethodFilter"/> and <see cref="WeaveTemplateCalleeCollector"/> exclusion rule tests.</summary>
public class WeaveMethodFilterTests
{
    /// <summary>After building fixtures, constructors of <see cref="Attribute"/> derived types should not be wildcard match candidates.</summary>
    [Fact]
    public void IsWildcardMatchCandidate_excludes_attribute_constructors()
    {
        FixtureBuildHelper.EnsureAllFixturesBuilt();
        Assert.True(File.Exists(FixtureBuildHelper.FixtureAssemblyPath));

        using var assembly = ReadFixtureAssembly();
        var module = assembly.MainModule;
        var attributeType = module.GetType("SharpWeaver.TestFixtures.Fake.FakeMetadataAttribute")
            ?? throw new InvalidOperationException("FakeMetadataAttribute type not found.");

        var ctor = attributeType.Methods.First(method => method.IsConstructor && method.HasBody);
        Assert.False(WeaveMethodFilter.IsWildcardMatchCandidate(ctor));
        Assert.True(WeaveMethodFilter.IsReflectionUnsafeWildcardTarget(ctor));
    }

    /// <summary>Normal game logic methods should still be eligible as wildcard match candidates.</summary>
    [Fact]
    public void IsWildcardMatchCandidate_allows_regular_methods()
    {
        FixtureBuildHelper.EnsureAllFixturesBuilt();
        Assert.True(File.Exists(FixtureBuildHelper.FixtureAssemblyPath));

        using var assembly = ReadFixtureAssembly();
        var module = assembly.MainModule;
        var panelType = module.GetType("SharpWeaver.TestFixtures.Fake.RegexPanelTarget")
            ?? throw new InvalidOperationException("RegexPanelTarget type not found.");

        var openMethod = panelType.Methods.First(method => method.Name == "_OnPanelOpen");
        Assert.True(WeaveMethodFilter.IsWildcardMatchCandidate(openMethod));
    }

    /// <summary>Methods in the target assembly directly called by weave templates should be added to the exclusion set.</summary>
    [Fact]
    public void WeaveTemplateCalleeCollector_includes_direct_callees_from_weave_templates()
    {
        FixtureBuildHelper.EnsureAllFixturesBuilt();

        using var assembly = ReadFixtureAssembly();
        WeaveScanner.Scan(assembly, out var weaves, out var scanErrors);
        Assert.Empty(scanErrors);

        var exclusions = WeaveTemplateCalleeCollector.Collect(weaves, assembly.MainModule);
        var infraTouch = assembly.MainModule
            .GetType("SharpWeaver.TestFixtures.Fake.WeaveCalleeInfra")
            ?.Methods.First(method => method.Name == "Touch")
            ?? throw new InvalidOperationException("WeaveCalleeInfra.Touch not found.");

        Assert.Contains(infraTouch, exclusions);
        Assert.False(WeaveMethodFilter.IsWildcardMatchCandidate(infraTouch, exclusions));
    }

    /// <summary>Broad wildcards may literally match infrastructure methods, but the exclusion set should filter them out.</summary>
    [Fact]
    public void Wildcard_match_candidate_excludes_weave_template_callees_even_when_wildcard_matches()
    {
        FixtureBuildHelper.EnsureAllFixturesBuilt();

        using var assembly = ReadFixtureAssembly();
        WeaveScanner.Scan(assembly, out var weaves, out var scanErrors);
        Assert.Empty(scanErrors);

        var exclusions = WeaveTemplateCalleeCollector.Collect(weaves, assembly.MainModule);
        var infraTouch = assembly.MainModule
            .GetType("SharpWeaver.TestFixtures.Fake.WeaveCalleeInfra")
            ?.Methods.First(method => method.Name == "Touch")
            ?? throw new InvalidOperationException("WeaveCalleeInfra.Touch not found.");

        Assert.True(
            WildcardSignatureParser.TryParse("SharpWeaver.TestFixtures.**.*.*(**)", out var pattern, out var parseError),
            parseError);
        Assert.True(WildcardSignatureMatcher.IsMatch(pattern!, infraTouch));
        Assert.False(WeaveMethodFilter.IsWildcardMatchCandidate(infraTouch, exclusions));
        Assert.True(WeaveMethodFilter.IsWildcardMatchCandidate(infraTouch));
    }

    /// <summary>Sync weaves marked to exclude async-like return types should not match methods that synchronously return Task.</summary>
    [Fact]
    public void IsSyncWeaveCandidate_can_exclude_async_like_return_methods_per_weave()
    {
        FixtureBuildHelper.EnsureAllFixturesBuilt();

        using var assembly = ReadFixtureAssembly();
        var target = assembly.MainModule
            .GetType("SharpWeaver.TestFixtures.Fake.AsyncTaskTarget")
            ?.Methods.First(method => method.Name == "SyncCompletedAsync")
            ?? throw new InvalidOperationException("SyncCompletedAsync not found.");
        var weaveMethod = assembly.MainModule
            .GetType("SharpWeaver.TestFixtures.ValidPatches")
            ?.Methods.First(method => method.Name == "FakeWorkWeave")
            ?? throw new InvalidOperationException("FakeWorkWeave not found.");
        Assert.True(SignaturePatternParser.TryParse("**.*.*(**)", out var pattern, out var parseError), parseError);

        var regularWeave = new WeaveInfo("**.*.*(**)", pattern!, 0, weaveMethod, weaveMethod.DeclaringType.FullName, 0);
        var excludeAsyncLikeReturnWeave = new WeaveInfo(
            "**.*.*(**)",
            pattern!,
            0,
            weaveMethod,
            weaveMethod.DeclaringType.FullName,
            0,
            excludeAsyncLikeReturn: true);

        Assert.True(WeaveMethodFilter.IsSyncWeaveCandidate(target, regularWeave));
        Assert.False(WeaveMethodFilter.IsSyncWeaveCandidate(target, excludeAsyncLikeReturnWeave));
    }

    private static AssemblyDefinition ReadFixtureAssembly()
    {
        var bytes = File.ReadAllBytes(FixtureBuildHelper.FixtureAssemblyPath);
        return AssemblyDefinition.ReadAssembly(
            new MemoryStream(bytes),
            new ReaderParameters { ReadingMode = ReadingMode.Immediate });
    }
}
