using System.Diagnostics;
using System.Reflection;
using SharpWeaver;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace SharpWeaver.Tests;

/// <summary>AsyncILWeaving filtering, IL injection, and runtime behavior tests.</summary>
public class IlAsyncWeavingTests
{
    private static readonly string FixturesOutputDir = FixtureBuildHelper.FixturesOutputDir;

    private static readonly string FixtureAssemblyPath = FixtureBuildHelper.FixtureAssemblyPath;

    /// <summary>Compiler-generated async methods should not be sync weave candidates.</summary>
    [Fact]
    public void IsSyncWeaveCandidate_excludes_compiler_async_methods()
    {
        FixtureBuildHelper.EnsureAllFixturesBuilt();
        using var assembly = ReadFixtureAssembly();
        var asyncMethod = GetMethod(assembly, "SharpWeaver.TestFixtures.Fake.AsyncTaskTarget", "SingleAwaitAsync");
        Assert.True(AsyncMethodHelper.IsCompilerAsyncMethod(asyncMethod));
        Assert.False(WeaveMethodFilter.IsSyncWeaveCandidate(asyncMethod));
    }

    /// <summary>Methods that synchronously return Task can still be sync weave candidates.</summary>
    [Fact]
    public void IsSyncWeaveCandidate_allows_sync_completed_task_methods()
    {
        FixtureBuildHelper.EnsureAllFixturesBuilt();
        using var assembly = ReadFixtureAssembly();
        var syncTaskMethod = GetMethod(assembly, "SharpWeaver.TestFixtures.Fake.AsyncTaskTarget", "SyncCompletedAsync");
        Assert.False(AsyncMethodHelper.IsCompilerAsyncMethod(syncTaskMethod));
        Assert.True(WeaveMethodFilter.IsSyncWeaveCandidate(syncTaskMethod));
    }

    /// <summary>Compiler-generated async GDTask methods should be async weave candidates.</summary>
    [Fact]
    public void IsAsyncWeaveCandidate_includes_compiler_async_gdtask_methods()
    {
        FixtureBuildHelper.EnsureAllFixturesBuilt();
        using var assembly = ReadFixtureAssembly();
        var asyncMethod = GetMethod(assembly, "SharpWeaver.TestFixtures.Fake.AsyncGdTaskTarget", "SingleAwaitAsync");
        Assert.True(AsyncMethodHelper.IsAsyncLikeReturn(asyncMethod.ReturnType));
        Assert.True(WeaveMethodFilter.IsAsyncWeaveCandidate(asyncMethod));
        Assert.True(AsyncMethodHelper.TryResolveMoveNext(asyncMethod, out _, out _));
    }

    /// <summary>Compiler-generated async Task&lt;T&gt; and GDTask&lt;T&gt; methods should be async weave candidates.</summary>
    [Fact]
    public void IsAsyncWeaveCandidate_includes_compiler_async_generic_task_methods()
    {
        FixtureBuildHelper.EnsureAllFixturesBuilt();
        using var assembly = ReadFixtureAssembly();

        var taskMethod = GetMethod(assembly, "SharpWeaver.TestFixtures.Fake.AsyncTaskTarget", "GenericAwaitAsync");
        var gdTaskMethod = GetMethod(assembly, "SharpWeaver.TestFixtures.Fake.AsyncGdTaskTarget", "GenericAwaitAsync");

        Assert.True(AsyncMethodHelper.IsAsyncLikeReturn(taskMethod.ReturnType));
        Assert.True(AsyncMethodHelper.IsAsyncLikeReturn(gdTaskMethod.ReturnType));
        Assert.True(WeaveMethodFilter.IsAsyncWeaveCandidate(taskMethod));
        Assert.True(WeaveMethodFilter.IsAsyncWeaveCandidate(gdTaskMethod));
    }

    /// <summary>After weaving, MoveNext should no longer contain the OriginalBodyAsync marker.</summary>
    [Fact]
    public void Weave_AsyncILWeaving_removes_marker_from_move_next()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var outerMethod = GetMethod(assembly, "SharpWeaver.TestFixtures.Fake.AsyncTaskTarget", "SingleAwaitAsync");
        Assert.True(AsyncMethodHelper.TryResolveMoveNext(outerMethod, out var moveNext, out _));

        Assert.DoesNotContain(moveNext.Body.Instructions, instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference methodReference
            && methodReference.Name == "OriginalBodyAsync");
    }

    /// <summary>Async weaving should execute in prefix → original body → postfix order.</summary>
    [Fact]
    public async Task Woven_AsyncILWeaving_runs_prefix_body_and_postfix_in_order()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.AsyncTaskTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var singleAwait = type.GetMethod("SingleAwaitAsync", BindingFlags.Instance | BindingFlags.Public)!;
        var task = (Task)singleAwait.Invoke(instance, null)!;
        await task.ConfigureAwait(true);

        Assert.Equal(1, behavioralState.AsyncPrefixRuns);
        Assert.Equal(1, behavioralState.AsyncBodyRuns);
        Assert.Equal(1, behavioralState.AsyncPostfixRuns);
        Assert.Equal(1, behavioralState.AsyncWeavePostfixRuns);
    }

    /// <summary>Async weave postfix that is an ordinary method call should still be cloned and executed.</summary>
    [Fact]
    public async Task Woven_AsyncILWeaving_runs_ordinary_method_call_postfix()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.AsyncOrdinaryPostfixTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var singleAwait = type.GetMethod("SingleAwaitAsync", BindingFlags.Instance | BindingFlags.Public)!;
        var task = (Task)singleAwait.Invoke(instance, null)!;
        await task.ConfigureAwait(true);

        Assert.Equal(1, behavioralState.AsyncPrefixRuns);
        Assert.Equal(1, behavioralState.AsyncBodyRuns);
        Assert.Equal(1, behavioralState.AsyncPostfixRuns);
        Assert.Equal(1, behavioralState.AsyncWeavePostfixRuns);
        Assert.Equal(
            ["SharpWeaver.TestFixtures.Fake.AsyncOrdinaryPostfixTarget", "SingleAwaitAsync"],
            behavioralState.AsyncTrace);
    }

    /// <summary>Async weave postfix located after a using statement should still be cloned and executed.</summary>
    [Fact]
    public async Task Woven_AsyncILWeaving_runs_postfix_after_using_statement()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.AsyncUsingStatementPostfixTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var singleAwait = type.GetMethod("SingleAwaitAsync", BindingFlags.Instance | BindingFlags.Public)!;
        var task = (Task)singleAwait.Invoke(instance, null)!;
        await task.ConfigureAwait(true);

        Assert.Equal(1, behavioralState.AsyncPrefixRuns);
        Assert.Equal(1, behavioralState.AsyncBodyRuns);
        Assert.Equal(1, behavioralState.AsyncPostfixRuns);
        Assert.Equal(1, behavioralState.AsyncDisposeRuns);
        Assert.Equal(1, behavioralState.AsyncWeavePostfixRuns);
    }

    /// <summary>Async methods with switch-based state dispatch should not misidentify the first user branch as the initial working area.</summary>
    [Fact]
    public async Task Woven_AsyncILWeaving_switch_dispatch_runs_prefix_before_user_body()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.AsyncSwitchDispatchTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var method = type.GetMethod("BranchBeforeFirstAwaitAsync", BindingFlags.Instance | BindingFlags.Public)!;
        var task = (Task)method.Invoke(instance, [false])!;
        await task.ConfigureAwait(true);

        Assert.Equal(["prefix", "body", "end", "weave_postfix"], behavioralState.AsyncTrace);
        Assert.Equal(1, behavioralState.AsyncPrefixRuns);
        Assert.Equal(1, behavioralState.AsyncBodyRuns);
        Assert.Equal(1, behavioralState.AsyncMidpointRuns);
        Assert.Equal(1, behavioralState.AsyncPostfixRuns);
        Assert.Equal(1, behavioralState.AsyncWeavePostfixRuns);
    }

    /// <summary>Async GDTask targets should be successfully woven and execute in order.</summary>
    [Fact]
    public async Task Woven_AsyncILWeaving_gdtask_target_runs_prefix_body_and_postfix()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.AsyncGdTaskTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var singleAwait = type.GetMethod("SingleAwaitAsync", BindingFlags.Instance | BindingFlags.Public)!;
        var gdtask = singleAwait.Invoke(instance, null)!;
        var awaiter = gdtask.GetType().GetMethod("GetAwaiter")!.Invoke(gdtask, null)!;
        awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, null);

        Assert.Equal(1, behavioralState.AsyncPrefixRuns);
        Assert.Equal(1, behavioralState.AsyncBodyRuns);
        Assert.Equal(1, behavioralState.AsyncPostfixRuns);
        Assert.Equal(1, behavioralState.AsyncWeavePostfixRuns);
        Assert.Equal(1, behavioralState.AsyncDisposeRuns);
    }

    /// <summary>Async GDTask multi-await targets should have no duplicate state machine fields and execute in order.</summary>
    [Fact]
    public void Woven_AsyncILWeaving_gdtask_multi_await_has_unique_state_machine_fields()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var outerMethod = GetMethod(assembly, "SharpWeaver.TestFixtures.Fake.AsyncGdTaskTarget", "MultiAwaitAsync");
        Assert.True(AsyncMethodHelper.TryResolveMoveNext(outerMethod, out _, out var stateMachine));

        var duplicateFieldNames = stateMachine.Fields
            .GroupBy(field => field.Name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.Empty(duplicateFieldNames);
        Assert.DoesNotContain(
            stateMachine.Fields,
            field => field.Name.StartsWith("<>u__", StringComparison.Ordinal)
                && field.FieldType.FullName == "System.Runtime.CompilerServices.TaskAwaiter");

        var wovenAssembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(wovenAssembly);
        behavioralState.Reset();

        var type = wovenAssembly.GetType("SharpWeaver.TestFixtures.Fake.AsyncGdTaskTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var multiAwait = type.GetMethod("MultiAwaitAsync", BindingFlags.Instance | BindingFlags.Public)!;
        var gdtask = multiAwait.Invoke(instance, null)!;
        var awaiter = gdtask.GetType().GetMethod("GetAwaiter")!.Invoke(gdtask, null)!;
        awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, null);

        Assert.Equal(1, behavioralState.AsyncPrefixRuns);
        Assert.Equal(1, behavioralState.AsyncBodyRuns);
        Assert.Equal(1, behavioralState.AsyncMidpointRuns);
        Assert.Equal(1, behavioralState.AsyncPostfixRuns);
        Assert.Equal(1, behavioralState.AsyncWeavePostfixRuns);
        Assert.Equal(1, behavioralState.AsyncDisposeRuns);
    }

    /// <summary>After weaving, leave instructions in MoveNext must have valid branch targets.</summary>
    [Fact]
    public void Woven_AsyncILWeaving_leave_targets_are_valid_on_single_await()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var outerMethod = GetMethod(assembly, "SharpWeaver.TestFixtures.Fake.AsyncGdTaskTarget", "SingleAwaitAsync");
        Assert.True(AsyncMethodHelper.TryResolveMoveNext(outerMethod, out var moveNext, out _));

        foreach (var instruction in moveNext.Body.Instructions)
        {
            if (instruction.OpCode != OpCodes.Leave && instruction.OpCode != OpCodes.Leave_S)
            {
                continue;
            }

            Assert.IsType<Instruction>(instruction.Operand);
        }
    }

    /// <summary>After async weaving, MoveNext should not retain short branches to avoid jump distance overflow after prefix insertion.</summary>
    [Fact]
    public void Woven_AsyncILWeaving_expands_short_branches_after_splicing()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        using var assembly = AssemblyDefinition.ReadAssembly(temp.AssemblyPath);
        var outerMethod = GetMethod(assembly, "SharpWeaver.TestFixtures.Fake.AsyncGdTaskTarget", "MultiAwaitAsync");
        Assert.True(AsyncMethodHelper.TryResolveMoveNext(outerMethod, out var moveNext, out _));

        Assert.DoesNotContain(moveNext.Body.Instructions, instruction => IsShortBranch(instruction.OpCode.Code));
    }

    /// <summary>Open generic async methods should only match generic-aware templates and capture runtime generic type arguments.</summary>
    [Fact]
    public async Task Woven_AsyncILWeaving_generic_method_captures_type_params()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.AsyncTaskTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var genericMethod = type.GetMethod("GenericMethodAsync")!.MakeGenericMethod(typeof(int));
        var task = (Task)genericMethod.Invoke(instance, [9])!;
        await task.ConfigureAwait(true);

        Assert.Equal(0, behavioralState.AsyncPrefixRuns);
        Assert.Equal(1, behavioralState.AsyncGenericPrefixRuns);
        Assert.Equal(1, behavioralState.AsyncGenericBodyRuns);
        Assert.Equal(1, behavioralState.AsyncGenericPostfixRuns);
        Assert.Equal(0, behavioralState.AsyncGenericWeavePostfixRuns);
        Assert.Equal("GenericMethodAsync", behavioralState.AsyncGenericCapturedMethodName);
        Assert.Equal(["Int32"], behavioralState.AsyncGenericCapturedTypeParamNames);
    }

    /// <summary>Open generic async GDTask&lt;T&gt; methods should preserve the return value and capture generic type arguments.</summary>
    [Fact]
    public void Woven_AsyncILWeaving_generic_gdtask_result_method_captures_type_params()
    {
        using var temp = CopyFixtureAssemblyToTemp();
        var references = BuildReferenceList();
        var exitCode = RunWeaver(temp.AssemblyPath, references, out var error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        var assembly = WovenAssemblyLoader.Load(temp.AssemblyPath, references);
        var behavioralState = GetBehavioralState(assembly);
        behavioralState.Reset();

        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.AsyncGdTaskTarget", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var genericMethod = type.GetMethod("GenericMethodResultAsync")!.MakeGenericMethod(typeof(int));
        var gdTask = genericMethod.Invoke(instance, [11])!;
        var awaiter = gdTask.GetType().GetMethod("GetAwaiter")!.Invoke(gdTask, null)!;
        var result = awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, null);

        Assert.Equal(11, result);
        Assert.Equal(1, behavioralState.AsyncGenericPrefixRuns);
        Assert.Equal(1, behavioralState.AsyncGenericBodyRuns);
        Assert.Equal(1, behavioralState.AsyncGenericPostfixRuns);
        Assert.Equal(["Int32"], behavioralState.AsyncGenericCapturedTypeParamNames);
    }

    private static MethodDefinition GetMethod(AssemblyDefinition assembly, string typeFullName, string methodName)
    {
        var type = assembly.MainModule.GetType(typeFullName)
            ?? throw new InvalidOperationException($"Type not found: {typeFullName}");
        return type.Methods.First(method => method.Name == methodName);
    }

    private static bool IsShortBranch(Code code) =>
        code is Code.Br_S
            or Code.Brfalse_S
            or Code.Brtrue_S
            or Code.Beq_S
            or Code.Bge_S
            or Code.Bgt_S
            or Code.Ble_S
            or Code.Blt_S
            or Code.Bne_Un_S
            or Code.Bge_Un_S
            or Code.Bgt_Un_S
            or Code.Ble_Un_S
            or Code.Blt_Un_S
            or Code.Leave_S;

    private static AssemblyDefinition ReadFixtureAssembly()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        return AssemblyDefinition.ReadAssembly(
            new MemoryStream(bytes),
            new ReaderParameters { ReadingMode = ReadingMode.Immediate });
    }

    private static TempAssemblyCopy CopyFixtureAssemblyToTemp()
    {
        FixtureBuildHelper.EnsureAllFixturesBuilt();
        var tempDir = Path.Combine(Path.GetTempPath(), "SharpWeaverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var assemblyName = Path.GetFileName(FixtureAssemblyPath);
        var targetAssembly = Path.Combine(tempDir, assemblyName);
        var targetPdb = Path.Combine(tempDir, Path.ChangeExtension(assemblyName, ".pdb"));
        File.Copy(FixtureAssemblyPath, targetAssembly, overwrite: true);
        var sourcePdb = Path.ChangeExtension(FixtureAssemblyPath, ".pdb");
        if (File.Exists(sourcePdb))
        {
            File.Copy(sourcePdb, targetPdb, overwrite: true);
        }

        return new TempAssemblyCopy(tempDir, targetAssembly, targetPdb);
    }

    private static List<string> BuildReferenceList()
    {
        var references = new List<string>();
        foreach (var file in Directory.GetFiles(FixturesOutputDir, "*.dll"))
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
        _ = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        error = stderr
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return process.ExitCode;
    }

    private static BehavioralStateAccessor GetBehavioralState(Assembly assembly)
    {
        var type = assembly.GetType("SharpWeaver.TestFixtures.Fake.BehavioralState", throwOnError: true)!;
        return new BehavioralStateAccessor(type);
    }

    private sealed class BehavioralStateAccessor(Type type)
    {
        public void Reset() => type.GetMethod("Reset")!.Invoke(null, null);

        public int AsyncPrefixRuns =>
            (int)type.GetProperty(nameof(AsyncPrefixRuns))!.GetValue(null)!;

        public int AsyncBodyRuns =>
            (int)type.GetProperty(nameof(AsyncBodyRuns))!.GetValue(null)!;

        public int AsyncPostfixRuns =>
            (int)type.GetProperty(nameof(AsyncPostfixRuns))!.GetValue(null)!;

        public int AsyncMidpointRuns =>
            (int)type.GetProperty(nameof(AsyncMidpointRuns))!.GetValue(null)!;

        public int AsyncWeavePostfixRuns =>
            (int)type.GetProperty(nameof(AsyncWeavePostfixRuns))!.GetValue(null)!;

        public int AsyncDisposeRuns =>
            (int)type.GetProperty(nameof(AsyncDisposeRuns))!.GetValue(null)!;

        public string[] AsyncTrace =>
            ((IEnumerable<string>)type.GetProperty(nameof(AsyncTrace))!.GetValue(null)!).ToArray();

        public int AsyncGenericBodyRuns =>
            (int)type.GetProperty(nameof(AsyncGenericBodyRuns))!.GetValue(null)!;

        public int AsyncGenericPostfixRuns =>
            (int)type.GetProperty(nameof(AsyncGenericPostfixRuns))!.GetValue(null)!;

        public int AsyncGenericPrefixRuns =>
            (int)type.GetProperty(nameof(AsyncGenericPrefixRuns))!.GetValue(null)!;

        public int AsyncGenericWeavePostfixRuns =>
            (int)type.GetProperty(nameof(AsyncGenericWeavePostfixRuns))!.GetValue(null)!;

        public string? AsyncGenericCapturedMethodName =>
            (string?)type.GetProperty(nameof(AsyncGenericCapturedMethodName))!.GetValue(null);

        public string[] AsyncGenericCapturedTypeParamNames =>
            (string[])type.GetProperty(nameof(AsyncGenericCapturedTypeParamNames))!.GetValue(null)!;
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
