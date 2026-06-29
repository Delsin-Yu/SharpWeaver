using System.Diagnostics;
using SharpWeaver;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace SharpWeaver.Tests;

/// <summary>Round 3 IL injection and PDB write-back tests.</summary>
[TestProgress]
public class IlInjectionTests
{
    private static readonly string DotnetProjectsDir = FixtureBuildHelper.ProjectRoot;

    private static readonly string FixturesOutputDir = FixtureBuildHelper.FixturesOutputDir;

    private static readonly string DuplicatePrefixOutputDir = Path.Combine(
        FixtureBuildHelper.TestsDirectory, "DuplicatePrefix", "bin", "Debug", "net10.0");

    private static readonly string FixtureAssemblyPath = FixtureBuildHelper.FixtureAssemblyPath;

    private static readonly string DuplicatePrefixAssemblyPath = Path.Combine(
        DuplicatePrefixOutputDir, "SharpWeaver.TestFixtures.DuplicatePrefix.dll");

    /// <summary>ILWeaving branch skip should emit a conditional branch before the marker.</summary>
    [Fact]
    public void Weave_ILWeaving_skip_emits_branch_before_original_body()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var type = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.IntReturnDerived");
        var method = type.Methods.First(m => m.Name == "GetValue");
        var instructions = method.Body.Instructions.ToList();

        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Brfalse
            || instruction.OpCode == OpCodes.Brfalse_S);
        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference methodReference
            && methodReference.Name == "get_HasValue");
    }

    /// <summary>
    /// After ILWeaving FakeLeaf.DoWork, the method body should no longer contain <c>OriginalBody</c> marker calls,
    /// and both <c>DoWorkTrace.Add</c> (original body) and <c>DoWorkPostfixRuns</c> (postfix) calls should exist in the same method.
    /// </summary>
    [Fact]
    public void Weave_ILWeaving_FakeLeaf_DoWork_removes_marker_and_contains_body_and_postfix()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var type = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.FakeLeaf");
        var method = type.Methods.First(m => m.Name == "DoWork");
        var instructions = method.Body.Instructions.ToList();

        Assert.DoesNotContain(instructions, instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference mr
            && mr.Name == "OriginalBody");

        Assert.Contains(instructions, instr =>
            (instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt)
            && instr.Operand?.ToString()?.Contains("DoWorkTrace", StringComparison.Ordinal) == true);

        Assert.Contains(instructions, instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand?.ToString()?.Contains("DoWorkPostfixRuns", StringComparison.Ordinal) == true);
    }

    /// <summary>After weaving, the DLL and PDB should be reloadable by Cecil.</summary>
    [Fact]
    public void Weave_writes_reloadable_dll_and_pdb()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.True(File.Exists(temp.PdbPath), $"PDB ????{temp.PdbPath}");

        var readerParameters = new ReaderParameters
        {
            ReadSymbols = true,
        };

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath, readerParameters);
        Assert.NotNull(assembly.MainModule);
        Assert.True(assembly.MainModule.HasSymbols);
    }

    /// <summary>AssemblyWeaver should skip abstract/extern method bodies.</summary>
    [Fact]
    public void AssemblyWeaver_skips_methods_without_body()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        using var resolver = new ReferenceAssemblyResolver(temp.AssemblyPath, references);
        WeaveScanner.Scan(resolver.WovenAssembly, out var weaves, out _);
        var registry = WeaveRegistry.Build(weaves, resolver, resolver.WovenAssembly.MainModule);

        var result = AssemblyWeaver.Weave(resolver.WovenAssembly, registry.Plans, resolver);
        Assert.True(result.Success);
        Assert.True(result.MethodsWoven > 0);
    }

    /// <summary>ILWeaving try/catch weave should preserve catch handlers on the override.</summary>
    [Fact]
    public void Weave_ILWeaving_exception_wrap_adds_catch_handler_from_weave_source()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var type = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.ThrowingDerived");
        var method = type.Methods.First(m => m.Name == "MayThrow");
        Assert.NotEmpty(method.Body.ExceptionHandlers);
        Assert.Contains(
            method.Body.ExceptionHandlers,
            handler => handler.HandlerType == ExceptionHandlerType.Catch
                && handler.CatchType?.FullName == "System.Exception");

        var instructions = method.Body.Instructions.ToList();
        var handlerIndex = instructions.FindIndex(i =>
            i.OpCode == OpCodes.Call
            && i.Operand is MethodReference mr
            && mr.Name == "set_ExceptionWrapHandlerRuns");
        Assert.True(handlerIndex >= 0);
    }

    /// <summary>ILWeaving TickWeave catch handler should wrap the original tick method body.</summary>
    [Fact]
    public void Weave_ILWeaving_TickWeave_catch_wraps_original_body()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var type = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.DerivedTickNode");
        var method = type.Methods.First(m => m.Name == "Tick");
        var handler = Assert.Single(
            method.Body.ExceptionHandlers,
            h => h.HandlerType == ExceptionHandlerType.Catch);
        var instructions = method.Body.Instructions.ToList();
        var tryStartIndex = instructions.IndexOf(handler.TryStart);
        var tryEndIndex = instructions.IndexOf(handler.TryEnd);
        Assert.True(tryStartIndex >= 0);
        Assert.True(tryEndIndex > tryStartIndex);
        Assert.DoesNotContain(instructions, instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference mr
            && mr.Name == "OriginalBody");
    }

    /// <summary>After multi-weave composition, the method body should not contain any <c>OriginalBody</c> markers.</summary>
    [Fact]
    public void Weave_ILWeaving_composition_removes_all_markers()
    {
        using var temp = CopyDuplicatePrefixAssemblyToTemp();
        var references = BuildReferenceList(DuplicatePrefixOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var type = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.DuplicatePrefix.DuplicateOverride");
        var method = type.Methods.First(m => m.Name == "TargetMethod");
        Assert.DoesNotContain(method.Body.Instructions, instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference mr
            && mr.Name == "OriginalBody");
    }

    /// <summary>
    /// The unwoven WeaveWorkWeave method should contain a <c>call</c> instruction to <c>WeaveTemplate.OriginalBody()</c>.
    /// </summary>
    [Fact]
    public void ILWeaving_OriginalBody_emits_call_instruction_in_unwoven_fixture()
    {
        EnsureFixturesBuilt();

        using var assembly = AssemblyDefinition.ReadAssembly(FixtureAssemblyPath);
        var type = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.ValidPatches");
        var method = type.Methods.First(m => m.Name == "WeaveWorkWeave");
        var instructions = method.Body.Instructions.ToList();

        Assert.Contains(instructions, instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference mr
            && mr.DeclaringType.FullName == "SharpWeaver.WeaveTemplate"
            && mr.Name == "OriginalBody");
    }

    /// <summary>
    /// After ILWeaving, the <c>WeaveLeaf.WeaveWork</c> method body should no longer contain <c>OriginalBody</c> calls.
    /// </summary>
    [Fact]
    public void Weave_ILWeaving_splice_removes_marker_call_from_override()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var type = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.WeaveLeaf");
        var method = type.Methods.First(m => m.Name == "WeaveWork");
        var instructions = method.Body.Instructions.ToList();

        Assert.DoesNotContain(instructions, instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference mr
            && mr.Name == "OriginalBody");
    }

    /// <summary>Regex weave should inject <c>ldstr</c> method name constants in the prefix.</summary>
    [Fact]
    public void Weave_regex_capture_injects_method_name_ldstr()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var type = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.RegexPanelTarget");
        var method = type.Methods.First(m => m.Name == "_OnPanelOpen");
        var instructions = method.Body.Instructions.ToList();

        Assert.Contains(instructions, instr =>
            instr.OpCode == OpCodes.Ldstr
            && instr.Operand is string s
            && s == "_OnPanelOpen");
        Assert.Contains(instructions, instr =>
            instr.OpCode == OpCodes.Ldstr
            && instr.Operand is string s
            && s == "SharpWeaver.TestFixtures.Fake.RegexPanelTarget");
    }

    /// <summary>
    /// After non-void wildcard weaving, the return value should be passed through <c>stloc</c>/<c>ldloc</c> slots instead of leaving values on the stack.
    /// </summary>
    [Fact]
    public void Weave_non_void_branch_to_ret_retargets_through_return_local_store()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var method = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.BranchReturnTarget")
            .Methods.First(m => m.Name == "Convert");
        var instructions = method.Body.Instructions.ToList();

        var retIndex = instructions.FindIndex(instr => instr.OpCode == OpCodes.Ret);
        Assert.True(retIndex > 0);

        var loadBeforeRet = instructions[retIndex - 1];
        Assert.True(
            loadBeforeRet.OpCode == OpCodes.Ldloc || loadBeforeRet.OpCode == OpCodes.Ldloc_S,
            $"?? ret ?? ldloc???? {loadBeforeRet.OpCode}??);

        var returnLocal = (VariableDefinition)loadBeforeRet.Operand!;
        Assert.Contains(
            instructions,
            instr => instr.OpCode == OpCodes.Stloc && ReferenceEquals(instr.Operand, returnLocal));
    }

    /// <summary>
    /// <c>init</c> setters (<c>void modreq(IsExternalInit)</c>) should be woven as void, without introducing void return local slots.
    /// </summary>
    [Fact]
    public void Weave_init_property_setter_does_not_emit_void_return_local()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var method = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.InitPropertyTarget")
            .Methods.First(m => m.Name == "set_Value");
        var instructions = method.Body.Instructions.ToList();

        Assert.DoesNotContain(
            method.Body.Variables,
            variable => IlTypeHelper.IsVoidReturn(variable.VariableType));

        var retIndex = instructions.FindIndex(instr => instr.OpCode == OpCodes.Ret);
        Assert.True(retIndex > 0);
        Assert.NotEqual(OpCodes.Ldloc, instructions[retIndex - 1].OpCode);
        Assert.NotEqual(OpCodes.Ldloc_S, instructions[retIndex - 1].OpCode);
    }

    /// <summary>Managed byref-return methods should not be sync-woven by wildcard profiling templates.</summary>
    [Fact]
    public void Weave_byref_return_method_is_not_woven_by_wildcard_template()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var method = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.ByRefReturnTarget`1")
            .Methods.First(m => m.Name == "GetValueRefOrNullRefReadOnly");

        Assert.True(IlTypeHelper.IsByReferenceReturn(method.ReturnType));
        Assert.DoesNotContain(method.Body.Instructions, instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference mr
            && mr.DeclaringType.FullName == "SharpWeaver.TestFixtures.ValidPatches"
            && mr.Name == "GenericCaptureWeave");
        Assert.DoesNotContain(method.Body.Instructions, instr =>
            instr.Operand is MethodReference mr
            && mr.DeclaringType.FullName == "SharpWeaver.TestFixtures.Fake.BehavioralState"
            && mr.Name == "set_GenericWeaveRuns");
    }

    /// <summary>Call-site weaving should remove template markers and keep the original callee call only when requested.</summary>
    [Fact]
    public void Weave_call_site_rewrites_callers_without_markers()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList(FixturesOutputDir);
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var type = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.CallSiteCallerTarget");
        var simple = type.Methods.First(m => m.Name == "RunSimple");
        var skip = type.Methods.First(m => m.Name == "RunSkipVoid");

        Assert.DoesNotContain(simple.Body.Instructions, instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference mr
            && mr.DeclaringType.FullName == "SharpWeaver.WeaveTemplate"
            && mr.Name == "OriginalBody");

        Assert.Contains(simple.Body.Instructions, instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference mr
            && mr.Name == "ExitSimple");

        Assert.DoesNotContain(skip.Body.Instructions, instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference mr
            && mr.Name == "ExitSkip");
    }

    private static TempAssemblyCopy CopyFixtureAssemblyToTemp()
    {
        EnsureFixturesBuilt();
        return CopyAssemblyToTemp(FixtureAssemblyPath);
    }

    private static TempAssemblyCopy CopyDuplicatePrefixAssemblyToTemp()
    {
        EnsureFixturesBuilt();
        return CopyAssemblyToTemp(DuplicatePrefixAssemblyPath);
    }

    private static TempAssemblyCopy CopyAssemblyToTemp(string sourceAssemblyPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SharpWeaverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var assemblyName = Path.GetFileName(sourceAssemblyPath);
        var pdbName = Path.ChangeExtension(assemblyName, ".pdb");
        var targetAssembly = Path.Combine(tempDir, assemblyName);
        var targetPdb = Path.Combine(tempDir, pdbName);

        File.Copy(sourceAssemblyPath, targetAssembly, overwrite: true);
        var sourcePdb = Path.ChangeExtension(sourceAssemblyPath, ".pdb");
        if (File.Exists(sourcePdb))
        {
            File.Copy(sourcePdb, targetPdb, overwrite: true);
        }

        return new TempAssemblyCopy(tempDir, targetAssembly, targetPdb);
    }

    private static void EnsureFixturesBuilt()
    {
        if (File.Exists(FixtureAssemblyPath) && File.Exists(DuplicatePrefixAssemblyPath))
        {
            return;
        }

        FixtureBuildHelper.EnsureAllFixturesBuilt();
    }

    private static List<string> BuildReferenceList(string primaryOutputDir)
    {
        var references = new List<string>();
        foreach (var file in Directory.GetFiles(primaryOutputDir, "*.dll"))
        {
            references.Add(file);
        }

        return references;
    }

    private static int RunWeaver(string assemblyPath, IReadOnlyList<string> references, out IReadOnlyList<string> error)
        => TestWeaverInvoker.RunWeave(assemblyPath, references, out error);

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
