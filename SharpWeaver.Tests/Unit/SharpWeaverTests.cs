using System.Diagnostics;
using SharpWeaver;
using Xunit;

namespace SharpWeaver.Tests;

/// <summary>Signature parsing, weave discovery, override matching, and dry-run tests.</summary>
public class SharpWeaverTests
{
    private static readonly string DotnetProjectsDir = FixtureBuildHelper.ProjectRoot;
    private static readonly string TestsDir = FixtureBuildHelper.TestsDirectory;

    private static readonly string FixturesProjectDir = Path.Combine(TestsDir, "Fixtures");

    private static readonly string FixturesOutputDir = Path.Combine(
        FixturesProjectDir, "bin", "Debug", "net10.0");

    private static readonly string BadSignatureOutputDir = Path.Combine(
        TestsDir, "BadSignature", "bin", "Debug", "net10.0");

    private static readonly string InstancePatchOutputDir = Path.Combine(
        TestsDir, "InstancePatch", "bin", "Debug", "net10.0");

    private static readonly string DuplicatePrefixOutputDir = Path.Combine(
        TestsDir, "DuplicatePrefix", "bin", "Debug", "net10.0");

    private static readonly string FixtureAssemblyPath = Path.Combine(
        FixturesOutputDir, "SharpWeaver.TestFixtures.dll");

    private static readonly string BadSignatureAssemblyPath = Path.Combine(
        BadSignatureOutputDir, "SharpWeaver.TestFixtures.BadSignature.dll");

    private static readonly string InstancePatchAssemblyPath = Path.Combine(
        InstancePatchOutputDir, "SharpWeaver.TestFixtures.InstancePatch.dll");

    private static readonly string DuplicatePrefixAssemblyPath = Path.Combine(
        DuplicatePrefixOutputDir, "SharpWeaver.TestFixtures.DuplicatePrefix.dll");

    private static readonly string GodotSharpPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nuget", "packages", "godotsharp", "4.6.1", "lib", "net8.0", "GodotSharp.dll");

    /// <summary>Builds test fixture assemblies.</summary>
    [Fact]
    public void Build_fixtures_succeeds()
    {
        EnsureAllFixturesBuilt();
        Assert.True(File.Exists(FixtureAssemblyPath), $"Fixture assembly not found: {FixtureAssemblyPath}");
    }

    /// <summary>Dry run should discover and list ILWeaving targets and overrides for <c>Godot.Node._Process(double)</c>.</summary>
    [Fact]
    public void DryRun_resolves_Godot_Node_Process_when_GodotSharp_in_references()
    {
        EnsureAllFixturesBuilt();
        Assert.True(File.Exists(GodotSharpPath), $"GodotSharp.dll not found at: {GodotSharpPath}");

        var references = BuildReferenceList(FixturesOutputDir, includeGodotSharp: true);
        var exitCode = RunDryRun(references, out var output, out var error);

        Assert.Equal(0, exitCode);
        Assert.Contains("SharpWeaver.TestFixtures.Godot.GodotProcessNode._Process(double)", output);
        Assert.Contains("ProcessWeave", output);
        Assert.Empty(error);
    }

    /// <summary>Unresolvable signatures should output actionable errors and return a non-zero exit code.</summary>
    [Fact]
    public void DryRun_unresolvable_signature_prints_actionable_error()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(BadSignatureOutputDir, includeGodotSharp: false);
        var exitCode = RunDryRun(references, out _, out var error, BadSignatureAssemblyPath);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("NoSuchType", string.Join(Environment.NewLine, error));
    }

    /// <summary>Non-static ILWeaving methods should be rejected (dry-run error, non-zero exit).</summary>
    [Fact]
    public void DryRun_non_static_ILWeaving_is_rejected()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(InstancePatchOutputDir, includeGodotSharp: true);
        var exitCode = RunDryRun(references, out _, out var error, InstancePatchAssemblyPath);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("static", string.Join(Environment.NewLine, error), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Fake hierarchy overrides should be matched by ILWeaving.</summary>
    [Fact]
    public void DryRun_matches_fake_hierarchy_overrides()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(FixturesOutputDir, includeGodotSharp: true);
        var exitCode = RunDryRun(references, out var output, out var error);

        Assert.Equal(0, exitCode);
        Assert.Contains("SharpWeaver.TestFixtures.Fake.FakeDerived.DoWork(int)", output);
        Assert.Contains("SharpWeaver.TestFixtures.Fake.FakeIntermediate.DoWork(int)", output);
        Assert.Contains("SharpWeaver.TestFixtures.Fake.FakeLeaf.DoWork(int)", output);
        Assert.Empty(error);
    }

    /// <summary>Override matching should ignore methods unrelated to the target signature.</summary>
    [Fact]
    public void DryRun_override_matching_ignores_unrelated_methods()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(FixturesOutputDir, includeGodotSharp: true);
        var exitCode = RunDryRun(references, out var output, out var error);

        Assert.Equal(0, exitCode);
        Assert.Contains("SharpWeaver.TestFixtures.Fake.FakeDerived.DoWork(int)", output);
        Assert.DoesNotContain("UnrelatedHelper", output);
        Assert.Empty(error);
    }

    /// <summary>Two <c>[Weave]</c> on the same target signature should be successfully discovered by dry-run (composition scenario is valid).</summary>
    [Fact]
    public void DryRun_two_ILWeaving_on_same_target_both_discovered()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(DuplicatePrefixOutputDir, includeGodotSharp: false);
        var exitCode = RunDryRun(references, out var output, out var error, DuplicatePrefixAssemblyPath);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("WeaveA", output, StringComparison.Ordinal);
        Assert.Contains("WeaveB", output, StringComparison.Ordinal);
        Assert.True(
            output.IndexOf("WeaveA", StringComparison.Ordinal) < output.IndexOf("WeaveB", StringComparison.Ordinal),
            "WeaveA 应在 WeaveB 之前出现（发现顺序）。");
    }

    /// <summary>Invalid wildcard patterns should fail during scanning.</summary>
    [Fact]
    public void DryRun_invalid_wildcard_pattern_fails()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(BadSignatureOutputDir, includeGodotSharp: false);
        var exitCode = RunDryRun(references, out _, out var error, BadSignatureAssemblyPath);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("**", string.Join(Environment.NewLine, error));
    }

    /// <summary>Wildcard patterns should match the <c>_OnPanelOpen</c> method.</summary>
    [Fact]
    public void DryRun_wildcard_matches_OnPanelOpen_method()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(FixturesOutputDir, includeGodotSharp: true);
        var exitCode = RunDryRun(references, out var output, out var error);

        Assert.Equal(0, exitCode);
        Assert.Contains("SharpWeaver.TestFixtures.Fake.RegexPanelTarget._OnPanelOpen()", output);
        Assert.DoesNotContain("SharpWeaver.TestFixtures.Fake.RegexPanelTarget.OtherMethod()", output);
        Assert.Empty(error);
    }

    /// <summary>Multiple <c>[Weave]</c> on the same weave method should each match different targets.</summary>
    [Fact]
    public void DryRun_multiple_ILWeaving_on_same_weave_method()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(FixturesOutputDir, includeGodotSharp: true);
        var exitCode = RunDryRun(references, out var output, out var error);

        Assert.Equal(0, exitCode);
        Assert.Contains("SharpWeaver.TestFixtures.Fake.MultiPatternTarget.Alpha()", output);
        Assert.Contains("SharpWeaver.TestFixtures.Fake.MultiPatternTarget.Beta()", output);
        Assert.Contains("MultiPatternWeave", output);
        Assert.Empty(error);
    }

    /// <summary>MethodWeavePlanner should order two weaves on the same target by priority.</summary>
    [Fact]
    public void MethodWeavePlanner_orders_weaves_by_priority()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(DuplicatePrefixOutputDir, includeGodotSharp: false);
        using var resolver = new ReferenceAssemblyResolver(DuplicatePrefixAssemblyPath, references);
        WeaveScanner.Scan(resolver.WovenAssembly, out var weaves, out _);
        var plannerResult = MethodWeavePlanner.Plan(weaves, resolver, resolver.WovenAssembly.MainModule);

        Assert.True(plannerResult.Success, $"Planner 不应有错误：{string.Join(", ", plannerResult.Errors)}");
        Assert.Single(plannerResult.Plans);
        var plan = plannerResult.Plans[0];
        Assert.Equal(2, plan.Weaves.Count);
        Assert.Equal("WeaveA", plan.Weaves[0].WeaveMethod.Name);
        Assert.Equal("WeaveB", plan.Weaves[1].WeaveMethod.Name);
        Assert.Equal(0, plan.Weaves[0].Priority);
        Assert.Equal(1, plan.Weaves[1].Priority);
    }

    /// <summary>WeaveRegistry should aggregate two ILWeavings with the same target signature into one binding ordered by priority.</summary>
    [Fact]
    public void WeaveRegistry_discovers_two_weaves_on_same_target_in_order()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(DuplicatePrefixOutputDir, includeGodotSharp: false);
        using var resolver = new ReferenceAssemblyResolver(DuplicatePrefixAssemblyPath, references);
        WeaveScanner.Scan(resolver.WovenAssembly, out var weaves, out _);
        var registry = WeaveRegistry.Build(weaves, resolver, resolver.WovenAssembly.MainModule);

        Assert.True(registry.Success, $"WeaveRegistry 不应有错误：{string.Join(", ", registry.Errors)}");
        Assert.Single(registry.Bindings);
        var binding = registry.Bindings[0];
        Assert.Equal(2, binding.Weaves.Count);
        Assert.Equal("WeaveA", binding.Weaves[0].WeaveMethod.Name);
        Assert.Equal("WeaveB", binding.Weaves[1].WeaveMethod.Name);
        Assert.Equal(0, binding.Weaves[0].Priority);
        Assert.Equal(1, binding.Weaves[1].Priority);
    }

    /// <summary>Non-static ILWeaving weave methods should be rejected by WeaveScanner.</summary>
    [Fact]
    public void WeaveScanner_rejects_non_static_ILWeaving_method()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(InstancePatchOutputDir, includeGodotSharp: true);
        using var resolver = new ReferenceAssemblyResolver(InstancePatchAssemblyPath, references);
        WeaveScanner.Scan(resolver.WovenAssembly, out _, out var scanErrors);

        Assert.Contains(scanErrors, e => e.Contains("static", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Unresolvable ILWeaving target signatures should produce actionable WeaveRegistry errors.</summary>
    [Fact]
    public void WeaveRegistry_reports_unresolvable_ILWeaving_signature()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(BadSignatureOutputDir, includeGodotSharp: false);
        using var resolver = new ReferenceAssemblyResolver(BadSignatureAssemblyPath, references);
        WeaveScanner.Scan(resolver.WovenAssembly, out var weaves, out _);
        var registry = WeaveRegistry.Build(weaves, resolver, resolver.WovenAssembly.MainModule);

        Assert.Contains(registry.Errors, error => error.Contains("NoSuchType", StringComparison.Ordinal));
    }

    /// <summary>Dry-run should list call-site matches by caller method and callee.</summary>
    [Fact]
    public void DryRun_call_site_lists_call_site_matches()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(FixturesOutputDir, includeGodotSharp: true);
        var exitCode = RunDryRun(references, out var output, out var error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("SharpWeaver.TestFixtures.Fake.CallSiteCallerTarget.RunSimple()", output);
        Assert.Contains("CallSite:", output);
        Assert.Contains("ExitSimple", output);
        Assert.Contains("ExitSimplePatch", output);
    }

    /// <summary>Dry-run should skip struct instance callees because their hidden receiver is a managed address.</summary>
    [Fact]
    public void DryRun_call_site_skips_value_type_instance_call_sites()
    {
        EnsureAllFixturesBuilt();

        var references = BuildReferenceList(FixturesOutputDir, includeGodotSharp: true);
        var exitCode = RunDryRun(references, out var output, out var error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.DoesNotContain("CallSiteStructCalleeTarget.Increment", output, StringComparison.Ordinal);
        Assert.DoesNotContain("StructInstancePatch", output, StringComparison.Ordinal);
    }

    private static void EnsureAllFixturesBuilt() => FixtureBuildHelper.EnsureAllFixturesBuilt();

    private static List<string> BuildReferenceList(string primaryOutputDir, bool includeGodotSharp)
    {
        var references = new List<string>();
        foreach (var file in Directory.GetFiles(primaryOutputDir, "*.dll"))
        {
            references.Add(file);
        }

        if (includeGodotSharp && File.Exists(GodotSharpPath))
        {
            references.Add(GodotSharpPath);
        }

        return references;
    }

    private static int RunDryRun(
        IReadOnlyList<string> references,
        out string output,
        out IReadOnlyList<string> error,
        string? assemblyPath = null)
    {
        var toolPath = Path.Combine(DotnetProjectsDir, "SharpWeaver", "bin", "Debug", "net10.0", "SharpWeaver.dll");
        var refsArg = string.Join(";", references);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments =
                $"\"{toolPath}\" --dry-run --assembly \"{assemblyPath ?? FixtureAssemblyPath}\" --references \"{refsArg}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        output = stdout;
        error = stderr
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return process.ExitCode;
    }
}
