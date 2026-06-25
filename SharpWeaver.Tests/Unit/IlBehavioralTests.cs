using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace SharpWeaver.Tests;

/// <summary>Post-weave runtime behavior tests (skip, postfix order, exception handling, composition order).</summary>
public class IlBehavioralTests
{
    private static readonly string DotnetProjectsDir = FixtureBuildHelper.ProjectRoot;
    private static readonly string TestsDir = FixtureBuildHelper.TestsDirectory;

    private static readonly string FixturesOutputDir = FixtureBuildHelper.FixturesOutputDir;

    private static readonly string FixtureAssemblyPath = FixtureBuildHelper.FixtureAssemblyPath;

    private static readonly string DuplicatePrefixOutputDir = Path.Combine(
        FixtureBuildHelper.TestsDirectory, "DuplicatePrefix", "bin", "Debug", "net10.0");

    private static readonly string DuplicatePrefixAssemblyPath = Path.Combine(
        DuplicatePrefixOutputDir, "SharpWeaver.TestFixtures.DuplicatePrefix.dll");

    private static readonly string GodotSharpPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nuget", "packages", "godotsharp", "4.6.1", "lib", "net8.0", "GodotSharp.dll");

    /// <summary>
    /// IntReturnWeave: when IntReturnPrefixValue has a value, skip the original body and return that value.
    /// </summary>
    [Fact]
    public void Woven_ILWeaving_skip_returns_prefix_value_when_set()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();
        behavioralState.IntReturnPrefixValue = 100;

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.IntReturnDerived", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var getValue = type.GetMethod("GetValue", BindingFlags.Instance | BindingFlags.Public)!;
        var result = (int)getValue.Invoke(instance, null)!;

        Assert.Equal(100, result);
    }

    /// <summary>
    /// IntReturnWeave: when IntReturnPrefixValue is null, execute the original body and return its value.
    /// </summary>
    [Fact]
    public void Woven_ILWeaving_skip_runs_original_body_when_prefix_value_null()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.IntReturnDerived", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var getValue = type.GetMethod("GetValue", BindingFlags.Instance | BindingFlags.Public)!;
        var result = (int)getValue.Invoke(instance, null)!;

        Assert.Equal(42, result);
    }

    /// <summary>FakeWorkWeave postfix should execute after the original method body.</summary>
    [Fact]
    public void Woven_postfix_runs_after_original_body_on_normal_path()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.FakeLeaf", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var doWork = type.GetMethod("DoWork", BindingFlags.Instance | BindingFlags.Public)!;
        doWork.Invoke(instance, [7]);

        Assert.Equal(1, behavioralState.DoWorkBodyRuns);
        Assert.Equal(1, behavioralState.DoWorkPostfixRuns);
        Assert.Equal(["body", "postfix"], behavioralState.DoWorkTrace);
    }

    /// <summary>MayThrowWeave: when the original body throws, the catch handler should execute and rethrow.</summary>
    [Fact]
    public void Woven_exception_handler_runs_when_original_body_throws()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.ThrowingDerived", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var mayThrow = type.GetMethod("MayThrow", BindingFlags.Instance | BindingFlags.Public)!;

        var thrown = Assert.Throws<TargetInvocationException>(() => mayThrow.Invoke(instance, [-1]));
        Assert.IsType<InvalidOperationException>(thrown.InnerException);
        Assert.Equal(1, behavioralState.ExceptionWrapHandlerRuns);
        Assert.Equal("boom", behavioralState.LastCaughtExceptionMessage);
        Assert.Equal(0, behavioralState.MayThrowBodyRuns);
    }

    /// <summary>MayThrowWeave: when the original body completes normally, the catch handler should not execute.</summary>
    [Fact]
    public void Woven_exception_handler_not_called_on_successful_body()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.ThrowingDerived", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var mayThrow = type.GetMethod("MayThrow", BindingFlags.Instance | BindingFlags.Public)!;
        mayThrow.Invoke(instance, [3]);

        Assert.Equal(0, behavioralState.ExceptionWrapHandlerRuns);
        Assert.Equal(1, behavioralState.MayThrowBodyRuns);
    }

    /// <summary>WeaveWorkWeave splice: after weaving, WeaveLeaf.WeaveWork should execute original body then postfix.</summary>
    [Fact]
    public void Woven_ILWeaving_splice_runs_original_body_then_postfix_in_order()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.WeaveLeaf", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var weaveWork = type.GetMethod("WeaveWork", BindingFlags.Instance | BindingFlags.Public)!;
        weaveWork.Invoke(instance, [7]);

        Assert.Equal(1, behavioralState.WeaveWorkBodyRuns);
        Assert.Equal(1, behavioralState.WeaveWorkPostfixRuns);
        Assert.Equal(["body", "weave_postfix"], behavioralState.WeaveWorkTrace);
    }

    /// <summary>
    /// Two ILWeaving compositions (onion model): outer WeaveB prefix first, inner WeaveA second, original body in the middle, postfixes inner→outer.
    /// </summary>
    [Fact]
    public void Woven_two_ILWeaving_composition_runs_in_onion_order()
    {
        EnsureAllFixturesBuilt();

        using var temp = CopyDuplicatePrefixAssemblyToTemp();
        var references = BuildDuplicatePrefixReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);

        var targetType = assembly.GetType(
            "SharpWeaver.TestFixtures.DuplicatePrefix.DuplicateTarget", throwOnError: true)!;
        targetType.GetMethod("Reset", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);

        var overrideType = assembly.GetType(
            "SharpWeaver.TestFixtures.DuplicatePrefix.DuplicateOverride", throwOnError: true)!;
        var instance = Activator.CreateInstance(overrideType)!;
        var targetMethod = overrideType.GetMethod("TargetMethod", BindingFlags.Instance | BindingFlags.Public)!;
        targetMethod.Invoke(instance, null);

        var trace = (List<string>)targetType
            .GetProperty("CompositionTrace", BindingFlags.Static | BindingFlags.Public)!
            .GetValue(null)!;

        Assert.Equal(["weaveB", "weaveA", "body", "weaveA_post", "weaveB_post"], trace);
    }

    /// <summary>Regex weave should only match <c>_OnPanelOpen</c>, not modify <c>OtherMethod</c>.</summary>
    [Fact]
    public void Woven_regex_weave_only_hits_matching_method()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.RegexPanelTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        type.GetMethod("_OnPanelOpen")!.Invoke(instance, null);
        type.GetMethod("OtherMethod")!.Invoke(instance, null);

        Assert.Equal(1, behavioralState.RegexPanelOpenRuns);
        Assert.Equal(1, behavioralState.RegexOtherMethodRuns);
        Assert.Equal("_OnPanelOpen", behavioralState.RegexCapturedMethodName);
        Assert.Equal("SharpWeaver.TestFixtures.Fake.RegexPanelTarget", behavioralState.RegexCapturedTypeName);
    }

    /// <summary><see cref="SharpWeaver.WeaveExcludeAttribute"/> should exclude specified targets from wildcard match results.</summary>
    [Fact]
    public void Woven_ILWeavingExclude_removes_target_from_wildcard_weave()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.WildcardExcludeTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        type.GetMethod("Included")!.Invoke(instance, null);
        type.GetMethod("Excluded")!.Invoke(instance, null);

        Assert.Equal(1, behavioralState.WildcardExcludeIncludedBodyRuns);
        Assert.Equal(1, behavioralState.WildcardExcludeExcludedBodyRuns);
        Assert.Equal(1, behavioralState.WildcardExcludeWeavePrefixRuns);
    }

    /// <summary>
    /// Non-void wildcard weave targets with branches converging on <c>ret</c> should still return the correct value (no InvalidProgramException).
    /// </summary>
    [Fact]
    public void Woven_wildcard_non_void_branch_to_ret_returns_correct_value()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.BranchReturnTarget", throwOnError: true)!;
        var convert = type.GetMethod("Convert")!;

        var withValue = convert.Invoke(null, [42]);
        var withoutValue = convert.Invoke(null, [null]);

        Assert.Equal(43, withValue);
        Assert.Null(withoutValue);
        Assert.Equal(2, behavioralState.BranchReturnBodyRuns);
    }

    /// <summary>
    /// <c>init</c> property setters should assign values normally after weaving, without throwing <see cref="InvalidProgramException"/>.
    /// </summary>
    [Fact]
    public void Woven_init_property_setter_assigns_value()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.InitPropertyTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type);
        var valueProperty = type.GetProperty("Value")!;
        valueProperty.SetValue(instance, 7);

        Assert.Equal(7, valueProperty.GetValue(instance));
        Assert.Equal(1, behavioralState.InitPropertySetterBodyRuns);
    }

    /// <summary>Open generic methods should only match generic-aware templates and capture runtime generic type arguments.</summary>
    [Fact]
    public void Woven_generic_method_uses_generic_weave_and_captures_type_params()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.GenericMethodTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var echo = type.GetMethod("Echo")!.MakeGenericMethod(typeof(int));
        var result = echo.Invoke(instance, [7]);

        Assert.Equal(7, result);
        Assert.Equal(1, behavioralState.GenericMethodBodyRuns);
        Assert.Equal(1, behavioralState.GenericWeaveRuns);
        Assert.Equal(0, behavioralState.GenericNonGenericWeaveRuns);
        Assert.Equal("Echo", behavioralState.GenericCapturedMethodName);
        Assert.Equal(["Int32"], behavioralState.GenericCapturedTypeParamNames);
    }

    /// <summary>Ordinary methods on open generic declaring types should also be handled by generic-aware templates.</summary>
    [Fact]
    public void Woven_generic_declaring_type_method_captures_declaring_type_params()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var openType = assembly.GetType("SharpWeaver.TestFixtures.Fake.GenericContainer`1", throwOnError: true)!;
        var closedType = openType.MakeGenericType(typeof(string));
        var instance = Activator.CreateInstance(closedType)!;
        var run = closedType.GetMethod("Run")!;
        var result = run.Invoke(instance, ["ok"]);

        Assert.Equal("ok", result);
        Assert.Equal(1, behavioralState.GenericDeclaringTypeBodyRuns);
        Assert.Equal(1, behavioralState.GenericWeaveRuns);
        Assert.Equal(0, behavioralState.GenericNonGenericWeaveRuns);
        Assert.Equal("Run", behavioralState.GenericCapturedMethodName);
        Assert.Equal(["String"], behavioralState.GenericCapturedTypeParamNames);
    }

    /// <summary>Non-generic templates should still match non-generic targets in the same namespace.</summary>
    [Fact]
    public void Woven_non_generic_method_in_generic_fixture_area_uses_regular_weave()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.GenericMethodTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var nonGenericEcho = type.GetMethod("NonGenericEcho")!;
        var result = nonGenericEcho.Invoke(instance, ["ok"]);

        Assert.Equal("ok", result);
        Assert.Equal(1, behavioralState.GenericNonGenericBodyRuns);
        Assert.Equal(0, behavioralState.GenericWeaveRuns);
        Assert.Equal(1, behavioralState.GenericNonGenericWeaveRuns);
    }

    /// <summary>Call-site should run prefix, original call, and postfix in order.</summary>
    [Fact]
    public void Woven_call_site_runs_prefix_original_and_postfix()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.CallSiteCallerTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        type.GetMethod("RunSimple")!.Invoke(instance, null);

        Assert.Equal(
            ["caller-before", "patch-start", "original-simple:0", "patch-end", "caller-after"],
            behavioralState.CallSiteTrace);
        Assert.Equal([0], behavioralState.CallSiteExitCodes);
    }

    /// <summary>Call-site should be able to mutate an argument before the original call.</summary>
    [Fact]
    public void Woven_call_site_mutates_argument()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.CallSiteCallerTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        type.GetMethod("RunParameterMutation")!.Invoke(instance, null);

        Assert.Equal(["patch-parameter", "original-parameter:42"], behavioralState.CallSiteTrace);
        Assert.Equal([42], behavioralState.CallSiteExitCodes);
    }

    /// <summary>Call-site without a marker should skip the original void call.</summary>
    [Fact]
    public void Woven_call_site_skips_void_original_call()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.CallSiteCallerTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        type.GetMethod("RunSkipVoid")!.Invoke(instance, null);

        Assert.Equal(["patch-skip-start", "patch-skip-end", "caller-continued"], behavioralState.CallSiteTrace);
        Assert.Empty(behavioralState.CallSiteExitCodes);
    }

    /// <summary>Call-site should conditionally skip or run the original call.</summary>
    [Fact]
    public void Woven_call_site_conditionally_skips_original_call()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.CallSiteCallerTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var runConditional = type.GetMethod("RunConditional")!;

        behavioralState.CallSiteSkipOriginal = true;
        runConditional.Invoke(instance, null);
        Assert.Equal(["patch-conditional", "caller-continued"], behavioralState.CallSiteTrace);
        Assert.Empty(behavioralState.CallSiteExitCodes);

        behavioralState.Reset();
        runConditional.Invoke(instance, null);
        Assert.Equal(["patch-conditional", "original-conditional:24", "caller-continued"], behavioralState.CallSiteTrace);
        Assert.Equal([24], behavioralState.CallSiteExitCodes);
    }

    /// <summary>Call-site should replace a non-void call through the trailing return slot.</summary>
    [Fact]
    public void Woven_call_site_replaces_return_value()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(includeGodotSharp: true);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.CallSiteCallerTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var result = type.GetMethod("RunReturnReplacement")!.Invoke(instance, null);

        Assert.Equal(42, result);
        Assert.Equal(["patch-return"], behavioralState.CallSiteTrace);
        Assert.Equal(0, behavioralState.CallSiteNextRuns);
    }

    private static BehavioralStateAccessor GetBehavioralState(Assembly assembly)
    {
        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.BehavioralState", throwOnError: true)!;
        return new BehavioralStateAccessor(type);
    }

    private sealed class BehavioralStateAccessor
    {
        private readonly Type _type;

        public BehavioralStateAccessor(Type type)
        {
            _type = type;
        }

        public int? IntReturnPrefixValue
        {
            get => (int?)_type.GetProperty(nameof(IntReturnPrefixValue))!.GetValue(null);
            set => _type.GetProperty(nameof(IntReturnPrefixValue))!.SetValue(null, value);
        }

        public int DoWorkBodyRuns =>
            (int)_type.GetProperty(nameof(DoWorkBodyRuns))!.GetValue(null)!;

        public int DoWorkPostfixRuns =>
            (int)_type.GetProperty(nameof(DoWorkPostfixRuns))!.GetValue(null)!;

        public List<string> DoWorkTrace =>
            (List<string>)_type.GetProperty(nameof(DoWorkTrace))!.GetValue(null)!;

        public int MayThrowBodyRuns =>
            (int)_type.GetProperty(nameof(MayThrowBodyRuns))!.GetValue(null)!;

        public int ExceptionWrapHandlerRuns =>
            (int)_type.GetProperty(nameof(ExceptionWrapHandlerRuns))!.GetValue(null)!;

        public string? LastCaughtExceptionMessage =>
            (string?)_type.GetProperty(nameof(LastCaughtExceptionMessage))!.GetValue(null);

        public int WeaveWorkBodyRuns =>
            (int)_type.GetProperty(nameof(WeaveWorkBodyRuns))!.GetValue(null)!;

        public int WeaveWorkPostfixRuns =>
            (int)_type.GetProperty(nameof(WeaveWorkPostfixRuns))!.GetValue(null)!;

        public List<string> WeaveWorkTrace =>
            (List<string>)_type.GetProperty(nameof(WeaveWorkTrace))!.GetValue(null)!;

        public int RegexPanelOpenRuns =>
            (int)_type.GetProperty(nameof(RegexPanelOpenRuns))!.GetValue(null)!;

        public int RegexOtherMethodRuns =>
            (int)_type.GetProperty(nameof(RegexOtherMethodRuns))!.GetValue(null)!;

        public string? RegexCapturedMethodName =>
            (string?)_type.GetProperty(nameof(RegexCapturedMethodName))!.GetValue(null);

        public string? RegexCapturedTypeName =>
            (string?)_type.GetProperty(nameof(RegexCapturedTypeName))!.GetValue(null);

        public int BranchReturnBodyRuns =>
            (int)_type.GetProperty(nameof(BranchReturnBodyRuns))!.GetValue(null)!;

        public int InitPropertySetterBodyRuns =>
            (int)_type.GetProperty(nameof(InitPropertySetterBodyRuns))!.GetValue(null)!;

        public int WildcardExcludeIncludedBodyRuns =>
            (int)_type.GetProperty(nameof(WildcardExcludeIncludedBodyRuns))!.GetValue(null)!;

        public int WildcardExcludeExcludedBodyRuns =>
            (int)_type.GetProperty(nameof(WildcardExcludeExcludedBodyRuns))!.GetValue(null)!;

        public int WildcardExcludeWeavePrefixRuns =>
            (int)_type.GetProperty(nameof(WildcardExcludeWeavePrefixRuns))!.GetValue(null)!;

        public int GenericMethodBodyRuns =>
            (int)_type.GetProperty(nameof(GenericMethodBodyRuns))!.GetValue(null)!;

        public int GenericDeclaringTypeBodyRuns =>
            (int)_type.GetProperty(nameof(GenericDeclaringTypeBodyRuns))!.GetValue(null)!;

        public int GenericNonGenericBodyRuns =>
            (int)_type.GetProperty(nameof(GenericNonGenericBodyRuns))!.GetValue(null)!;

        public int GenericWeaveRuns =>
            (int)_type.GetProperty(nameof(GenericWeaveRuns))!.GetValue(null)!;

        public int GenericNonGenericWeaveRuns =>
            (int)_type.GetProperty(nameof(GenericNonGenericWeaveRuns))!.GetValue(null)!;

        public string? GenericCapturedMethodName =>
            (string?)_type.GetProperty(nameof(GenericCapturedMethodName))!.GetValue(null);

        public string[] GenericCapturedTypeParamNames =>
            (string[])_type.GetProperty(nameof(GenericCapturedTypeParamNames))!.GetValue(null)!;

        public List<string> CallSiteTrace =>
            (List<string>)_type.GetProperty(nameof(CallSiteTrace))!.GetValue(null)!;

        public List<int> CallSiteExitCodes =>
            (List<int>)_type.GetProperty(nameof(CallSiteExitCodes))!.GetValue(null)!;

        public bool CallSiteSkipOriginal
        {
            get => (bool)_type.GetProperty(nameof(CallSiteSkipOriginal))!.GetValue(null)!;
            set => _type.GetProperty(nameof(CallSiteSkipOriginal))!.SetValue(null, value);
        }

        public int CallSiteNextRuns =>
            (int)_type.GetProperty(nameof(CallSiteNextRuns))!.GetValue(null)!;

        public void Reset() => _type.GetMethod(nameof(Reset))!.Invoke(null, null);
    }

    private static TempAssemblyCopy CopyFixtureAssemblyToTemp()
    {
        EnsureAllFixturesBuilt();
        return CopyAssemblyToTemp(FixtureAssemblyPath);
    }

    private static TempAssemblyCopy CopyDuplicatePrefixAssemblyToTemp()
    {
        EnsureAllFixturesBuilt();
        return CopyAssemblyToTemp(DuplicatePrefixAssemblyPath);
    }

    private static TempAssemblyCopy CopyAssemblyToTemp(string assemblyPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SharpWeaverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var assemblyName = Path.GetFileName(assemblyPath);
        var pdbName = Path.ChangeExtension(assemblyName, ".pdb");
        var targetAssembly = Path.Combine(tempDir, assemblyName);
        var targetPdb = Path.Combine(tempDir, pdbName);

        File.Copy(assemblyPath, targetAssembly, overwrite: true);
        var sourcePdb = Path.ChangeExtension(assemblyPath, ".pdb");
        if (File.Exists(sourcePdb))
        {
            File.Copy(sourcePdb, targetPdb, overwrite: true);
        }

        return new TempAssemblyCopy(tempDir, targetAssembly, targetPdb);
    }

    private static void EnsureAllFixturesBuilt()
    {
        if (File.Exists(FixtureAssemblyPath) && File.Exists(DuplicatePrefixAssemblyPath))
        {
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments =
                $"build \"{Path.Combine(TestsDir, "SharpWeaver.Tests", "SharpWeaver.Tests.csproj")}\" -c Debug",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static List<string> BuildReferenceList(bool includeGodotSharp)
    {
        var references = new List<string>();
        foreach (var file in Directory.GetFiles(FixturesOutputDir, "*.dll"))
        {
            references.Add(file);
        }

        if (includeGodotSharp && File.Exists(GodotSharpPath))
        {
            references.Add(GodotSharpPath);
        }

        return references;
    }

    private static List<string> BuildDuplicatePrefixReferenceList()
    {
        var references = new List<string>();
        foreach (var file in Directory.GetFiles(DuplicatePrefixOutputDir, "*.dll"))
        {
            references.Add(file);
        }

        return references;
    }

    private static int RunWeaver(string assemblyPath, IReadOnlyList<string> references, out IReadOnlyList<string> error)
    {
        var toolPath = FixtureBuildHelper.WeaverToolPath;
        var refsArg = string.Join(";", references);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{toolPath}\" --assembly \"{assemblyPath}\" --references \"{refsArg}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        _ = stdout;
        error = stderr
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return process.ExitCode;
    }

    private sealed class TempAssemblyCopy : IDisposable
    {
        public TempAssemblyCopy(string directory, string assemblyPath, string pdbPath)
        {
            Directory = directory;
            AssemblyPath = assemblyPath;
            PdbPath = pdbPath;
        }

        public string Directory { get; }

        public string AssemblyPath { get; }

        public string PdbPath { get; }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                {
                    System.IO.Directory.Delete(Directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
