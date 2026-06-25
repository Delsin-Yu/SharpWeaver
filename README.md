<a id="en-us"></a>

# SharpWeaver

> **English** | [中文](#zh-hans)

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Compile-time IL weaver for .NET. Write normal C# weave templates with `[Weave]`, `[AsyncWeave]`, and `[WeaveCallSite]`; SharpWeaver splices them into target methods after `CoreCompile` using [Mono.Cecil](https://github.com/jbevain/cecil).

| | |
| --- | --- |
| **Attribute-driven weaving** | Prefix, postfix, exception handling, and control flow in plain C# — no hand-written IL |
| **Signature matching** | Exact CLR signatures and structured wildcards for single methods or whole families |
| **Sync & async** | `WeaveTemplate.OriginalBody()` for sync; `await WeaveTemplate.OriginalBodyAsync()` into state-machine `MoveNext` |
| **Call-site weaving** | Wrap calls to methods declared in other assemblies without rewriting the callee assembly |
| **MSBuild integration** | Runs after compile in Debug by default; incremental builds use stamp sidecars |
| **Metadata capture** | Type name, method name, line number, and file path injected from PDB at weave time |

---

## Table of Contents

**Overview**

- [Abstract](#abstract)
- [Packages](#packages)

**Getting started**

- [Installation](#installation)
- [Basic API usage](#basic-api-usage)
- [Examples](#examples)

**Weaving model**

- [Signature patterns](#signature-patterns)
- [Weave method contracts](#weave-method-contracts)
- [Priority and layering](#priority-and-layering)
- [Wildcard weaving](#wildcard-weaving)
- [Call-site weaving](#call-site-weaving)
- [Async weaving](#async-weaving)
- [Override matching](#override-matching)
- [Weaving shenanigans](#weaving-shenanigans)

**Build & operations**

- [MSBuild integration](#msbuild-integration)
- [CLI](#cli)
- [Troubleshooting](#troubleshooting)
- [Tests](#tests)
- [License](#license)

---

## Abstract

SharpWeaver is a post-compile IL weaver for .NET assemblies. You define **weave templates** — ordinary `static` methods annotated with `[Weave]` or `[AsyncWeave]` — and call `WeaveTemplate.OriginalBody()` (or `await WeaveTemplate.OriginalBodyAsync()`) as a marker where the original target body should be inlined.

At build time, SharpWeaver:

1. Scans the compiled assembly for weave templates and their target signatures.
2. Matches targets by exact CLR signature or structured wildcard pattern.
3. Splices template prefix/postfix IL around an inline copy of each target's original body.
4. Rewrites branches, exception handlers, and (for async) state machine `MoveNext` IL so the result is valid and runnable.

Because templates are real C#, you can use `try/catch`, early returns, `using`, loops, and `await` — the weaver handles IL relocation and type rewriting for you.

---

## Packages

| Project | Role |
| :------ | :--- |
| `SharpWeaver.Attributes` | `[Weave]`, `[AsyncWeave]`, `[WeaveCallSite]`, capture attributes, `WeaveTemplate` markers |
| `SharpWeaver.Build` | MSBuild targets (`SharpWeave` after compile) |
| `SharpWeaver` | CLI weaver (`dotnet exec SharpWeaver.dll`) |
| `SharpWeaver.Examples` | Runnable cookbook — one scenario per source file |

---

## Installation

Reference the attributes project and import MSBuild targets in your `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="path\to\SharpWeaver.Attributes\SharpWeaver.Attributes.csproj" />
</ItemGroup>

<Import Project="path\to\SharpWeaver.Build\build\SharpWeaver.targets"
        Condition="Exists('path\to\SharpWeaver.Build\build\SharpWeaver.targets')" />
```

Build in **Debug** (weaving enabled by default) or pass `-p:SharpWeaverEnabled=true`.

NuGet packages are planned for a future release; until then, use project references as shown above.

---

## Basic API usage

Define weave templates in the **same assembly** as the methods you want to patch.

### Prefix / postfix

```csharp
using SharpWeaver;

public static class MyPatches
{
    [Weave("MyApp.Services.MyService.DoWork(int)", priority: 0)]
    public static void DoWorkWeave(MyService instance, ref int value)
    {
        Console.WriteLine("before");
        WeaveTemplate.OriginalBody();
        Console.WriteLine("after");
    }
}
```

### Exception handling

```csharp
[Weave("Godot.Node._Process(double)", priority: 0)]
public static void ProcessWeave(Node instance, ref double delta)
{
    try
    {
        WeaveTemplate.OriginalBody();
    }
    catch (Exception ex)
    {
        PublishProcessException(ex, instance);
        throw;
    }
}
```

### Early return skip

Non-void targets only — append `ref TReturn returnValue` and return before `OriginalBody()`:

```csharp
[Weave("MyApp.Counter.GetValue()", priority: 0)]
public static void GetValueWeave(Counter instance, ref int returnValue)
{
    if (ShouldSkip())
    {
        returnValue = 42;
        return;
    }

    WeaveTemplate.OriginalBody();
}
```

### Async weaving

Template side uses BCL `Task`; the weaver rewrites IL to the target async return type (see [Async weaving](#async-weaving)):

```csharp
[AsyncWeave("MyApp.Services.**.*Async(**)", priority: 0)]
public static async Task ServiceAsyncWeave(
    object? instance,
    [WeaveTypeName] string typeName,
    [WeaveMethodName] string methodName)
{
    LogEnter(typeName, methodName);
    await WeaveTemplate.OriginalBodyAsync();
    LogExit(typeName, methodName);
}
```

### Call-site weaving

Use `[WeaveCallSite]` when the signature you want to match is the **callee** at each `call` / `callvirt` instruction, rather than the body being woven. This is useful for wrapping framework or third-party methods from your own assembly:

```csharp
[WeaveCallSite("ThirdParty.Api.Client.Send(string)", priority: 0)]
public static void SendCallWeave(Client instance, ref string message)
{
    Console.WriteLine("[before send]");
    WeaveTemplate.OriginalBody();
    Console.WriteLine("[after send]");
}
```

For more examples, see **[SharpWeaver.Examples](#examples)** and the [unit tests / fixtures](SharpWeaver.Tests/) (`ValidPatches.cs`, `ValidAsyncPatches.cs`, `CallSitePatches.cs`).

---

## Examples

The [`SharpWeaver.Examples`](SharpWeaver.Examples/) project is a runnable cookbook. **Each file contains both the user code (target types/methods) and the weave patch** for one scenario.

Build and run (Debug weaving is on by default):

```powershell
dotnet run --project SharpWeaver.Examples/SharpWeaver.Examples.csproj
```

| File | What it demonstrates |
| :--- | :------------------- |
| [PrefixPostfixLogging.cs](SharpWeaver.Examples/PrefixPostfixLogging.cs) | Wildcard sync weave with prefix/postfix logging |
| [ExceptionWrap.cs](SharpWeaver.Examples/ExceptionWrap.cs) | `try/catch` around `WeaveTemplate.OriginalBody()` |
| [EarlyReturnSkip.cs](SharpWeaver.Examples/EarlyReturnSkip.cs) | Exact-mode skip via trailing `ref TReturn returnValue` on a virtual override |
| [WildcardInstrumentation.cs](SharpWeaver.Examples/WildcardInstrumentation.cs) | Wildcard match + `[WeaveTypeName]` / `[WeaveMethodName]` / PDB capture |
| [WeaveExclude.cs](SharpWeaver.Examples/WeaveExclude.cs) | Narrow a wildcard with `[WeaveExclude]` |
| [MultiTargetWeave.cs](SharpWeaver.Examples/MultiTargetWeave.cs) | Multiple `[Weave]` attributes on one patch method |
| [PriorityLayering.cs](SharpWeaver.Examples/PriorityLayering.cs) | Two weaves on one target — onion composition by priority |
| [StaticMethodWeave.cs](SharpWeaver.Examples/StaticMethodWeave.cs) | Wildcard on a static method (`object? instance` → `ldnull`) |
| [VirtualOverrideWeave.cs](SharpWeaver.Examples/VirtualOverrideWeave.cs) | Exact signature on a base virtual method weaves overrides |
| [AsyncTaskWeave.cs](SharpWeaver.Examples/AsyncTaskWeave.cs) | `[AsyncWeave]` on `Task` async methods |
| [GenericWeaveCapture.cs](SharpWeaver.Examples/GenericWeaveCapture.cs) | `genericWeave: true` + `[WeaveTypeParams]` |

### Prefix / postfix logging

From [PrefixPostfixLogging.cs](SharpWeaver.Examples/PrefixPostfixLogging.cs):

```csharp
namespace SharpWeaver.Examples.PrefixPostfixLogging;

public sealed class Greeter
{
    public void SayHello(string name) => Console.WriteLine($"Hello, {name}!");
}

public static class GreeterWeavePatch
{
    [Weave("SharpWeaver.Examples.PrefixPostfixLogging.Greeter.SayHello(**)", priority: 0)]
    public static void SayHelloWeave(object? instance)
    {
        Console.WriteLine("[enter] SayHello");
        WeaveTemplate.OriginalBody();
        Console.WriteLine("[leave] SayHello");
    }
}
```

### Exception wrapping

From [ExceptionWrap.cs](SharpWeaver.Examples/ExceptionWrap.cs):

```csharp
[Weave("SharpWeaver.Examples.ExceptionWrap.Calculator.Divide(**)", priority: 0)]
public static void DivideWeave(object? instance)
{
    try
    {
        WeaveTemplate.OriginalBody();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[error] Divide failed: {ex.Message}");
        throw;
    }
}
```

### Early return skip (exact mode + override)

Exact signatures resolve **overrides in the woven assembly**, not the declared method itself when no override exists. See [EarlyReturnSkip.cs](SharpWeaver.Examples/EarlyReturnSkip.cs):

```csharp
public class CounterBase
{
    public virtual int GetNext() => 1;
}

public sealed class Counter : CounterBase
{
    private int _value;
    public override int GetNext() => ++_value;
}

[Weave("SharpWeaver.Examples.EarlyReturnSkip.CounterBase.GetNext()", priority: 0)]
public static void GetNextWeave(CounterBase instance, ref int returnValue)
{
    if (CounterWeavePatch.CachedValue is { } cached)
    {
        returnValue = cached;
        return;
    }

    WeaveTemplate.OriginalBody();
}
```

### Wildcard instrumentation + capture

From [WildcardInstrumentation.cs](SharpWeaver.Examples/WildcardInstrumentation.cs):

```csharp
[Weave("SharpWeaver.Examples.WildcardInstrumentation.*.*(int)", priority: 5)]
public static void InstrumentWeave(
    object? instance,
    [WeaveTypeName] string typeName,
    [WeaveMethodName] string methodName,
    [WeaveLineNumber] int lineNumber,
    [WeaveFilePath] string filePath)
{
    Console.WriteLine($"[trace] {typeName}.{methodName} @ {filePath}:{lineNumber}");
    WeaveTemplate.OriginalBody();
}
```

> **Tip:** Broad `(**)` wildcards also match constructors and property accessors. Prefer narrower parameter patterns (as above) or add `[WeaveExclude]` rules.

### Priority onion (two weaves, one target)

From [PriorityLayering.cs](SharpWeaver.Examples/PriorityLayering.cs). Runtime order: **outer prefix → inner prefix → body → inner postfix → outer postfix**.

```csharp
[Weave("...LayeredService.Execute(**)", priority: 0)]   // inner — woven first
public static void InnerWeave(object? instance) { /* ... */ }

[Weave("...LayeredService.Execute(**)", priority: 10)]  // outer — woven second, wraps outside
public static void OuterWeave(object? instance) { /* ... */ }
```

### Async Task weaving

From [AsyncTaskWeave.cs](SharpWeaver.Examples/AsyncTaskWeave.cs):

```csharp
[AsyncWeave("SharpWeaver.Examples.AsyncTaskWeave.AsyncWorker.RunAsync(**)", priority: 0)]
public static async Task RunAsyncWeave(object? instance)
{
    Console.WriteLine("[async] prefix");
    await WeaveTemplate.OriginalBodyAsync();
    Console.WriteLine("[async] postfix");
}
```

---

## Signature patterns

Target signatures use the form `FullTypeName.MethodName(ParamTypes...)`, aligned with GodotSharp-style XML documentation names.

### Exact mode

No `*` or `**` in the pattern. Matches a single CLR method signature.

Example: `Godot.Node._Process(double)`

In exact mode, SharpWeaver resolves the declared signature, then **weaves matching overrides in the same assembly**. It does not splice into the declared method when that method has no override in the assembly — use wildcard mode for arbitrary instance/static methods, or pair exact mode with a virtual base + override (see [EarlyReturnSkip.cs](SharpWeaver.Examples/EarlyReturnSkip.cs)).

### Wildcard mode

Contains `*` or `**`. Performs structured matching against weaveable methods in the assembly.

| Region | Token | Meaning |
| :----- | :---- | :------ |
| Namespace segment | literal | Exact segment match |
| Namespace segment | `*` | Exactly one segment |
| Namespace segment | `**` | Zero or more segments |
| Namespace segment | `*text` / `text*` | Character-level glob within a segment |
| Type / method name | `*` | Any name |
| Type / method name | glob | Character-level glob (type/method names cannot be `**` alone) |
| Parameters | literal | Exact type (same aliases as exact mode) |
| Parameters | `*` | Exactly one parameter type |
| Parameters | `**` | Zero or more parameter types; `(**)` alone matches all overloads |

Invalid wildcard syntax fails at scan time and fails the build.

Examples:

| Pattern | Matches |
| :------ | :------ |
| `MyApp.Services.**.*.*(**)` | Any method on any type under `MyApp.Services` and nested namespaces |
| `MyApp.**.*Async(**)` | Methods whose names end with `Async` |
| `MyApp.MyType.DoWork(int)` | Exact method (same as exact mode) |

---

## Weave method contracts

### Synchronous `[Weave]`

| Mode | Requirements |
| :--- | :------------- |
| **Exact** | `static void`; first parameter is the target instance type (by value); remaining parameters mirror target parameters. Non-void targets may append `ref TReturn returnValue` for early-return skip. Exactly one `WeaveTemplate.OriginalBody()`. |
| **Wildcard** | Optional first parameter `object? instance` (becomes `ldnull` for static targets). Zero or more capture parameters (each at most once). No per-target `ref` slots; no `ref TReturn`. Exactly one `WeaveTemplate.OriginalBody()`. |

Compiler-generated async methods (`[AsyncStateMachine]`) are excluded from sync weaving — use `[AsyncWeave]` instead.

Methods that synchronously return async-like types (see [Supported async-like return types](#async-weaving)) can be excluded with `[WeaveExcludeAsyncLikeReturn]`.

### Asynchronous `[AsyncWeave]`

- `static async Task` or `static async Task<T>` on the template side.
- Exactly one `await WeaveTemplate.OriginalBodyAsync()` in the template body.
- Target must be a compiler-generated async method returning an async-like type.
- Weaving happens on the state machine's `MoveNext`; metadata capture reads the outer async method.
- Wildcard capture parameters follow the same rules as sync wildcard weaving.

Synchronous methods that return `Task` or `ValueTask` without a state machine (e.g. `return Task.CompletedTask`) are still sync weave targets.

### Call-site `[WeaveCallSite]`

- `static void`; the template must be synchronous.
- The target signature matches the called method, not the body being woven.
- `WeaveTemplate.OriginalBody()` may appear zero or one time. If reached, the original call is emitted at that point; returning before it skips the call.
- Parameters map to the call stack order: instance receiver first for instance calls, followed by callee arguments. Parameters may be `ref T` to mutate values passed to the original call.
- Non-void callees may append `ref TReturn returnValue` after the complete receiver/argument list to replace the returned value.
- v1 skips value-type instance callees because their hidden receiver is a managed address in IL; future support needs a dedicated byref receiver slot.

### Capture attributes

Use on wildcard weave parameters; SharpWeaver injects values at splice time:

| Attribute | Injected value |
| :-------- | :------------- |
| `[WeaveTypeName]` | `DeclaringType.FullName` |
| `[WeaveMethodName]` | Method name |
| `[WeaveLineNumber]` | First non-hidden PDB sequence point line, or `0` |
| `[WeaveFilePath]` | First PDB document path, or `""` |
| `[WeaveTypeParams]` | Open generic parameter `Type[]` on the target method |

### Exclusions

| Attribute | Purpose |
| :-------- | :------ |
| `[WeaveExclude(excludedSignature)]` | Remove matching targets from a weave method's wildcard enumeration (same exact/wildcard syntax). |
| `[WeaveExcludeAsyncLikeReturn]` | Skip sync targets that return async-like types. |

Broad wildcard patterns should exclude `*ctor`, `get_*`, `set_*`, and your own patch/helper types to avoid recursion and low-value instrumentation.

Wildcard mode also automatically excludes:

- All methods on types inheriting `System.Attribute` (prevents recursion when reflection reads attributes).
- Direct callees referenced from weave template bodies within the woven assembly.

### Weaveable method filter

| | |
| --- | --- |
| **Included** | Methods with IL bodies; not `abstract`, `extern`, or P/Invoke |
| **Excluded** | Weave template methods themselves; compiler-generated names (`<`, `>`, `__` prefixes) |

---

## Priority and layering

`priority` is a **required** constructor argument on `[Weave]`, `[AsyncWeave]`, and `[WeaveCallSite]`.

> **Runtime order:** outer prefix → inner prefix → body → inner postfix → outer postfix

- Weaves are **applied in ascending priority order** during composition (lower number first).
- Lower priority ends up **closer to the original body** (inner layer); higher priority is woven later and wraps **outside**.
- At runtime, prefixes run **outer → inner → body**, postfixes run **inner → outer**.
- Equal priority: discovery order.
- One target can match multiple weaves; one weave method can carry multiple attributes (`AllowMultiple = true`).

```csharp
[Weave("MyApp.**.*(**)", priority: 0)]   // inner — closer to the original body
public static void InnerWeave(object? instance) { /* ... */ WeaveTemplate.OriginalBody(); }

[Weave("MyApp.**.*(**)", priority: 10)]  // outer — higher priority wraps outside
public static void OuterWeave(object? instance) { /* ... */ WeaveTemplate.OriginalBody(); }
```

---

## Wildcard weaving

Full instrumentation example with metadata capture:

```csharp
[Weave("MyApp.Services.**.*.*(**)", priority: 10)]
[WeaveExclude("MyApp.Services.Internal.*.*(**)")]
public static void InstrumentWeave(
    object? instance,
    [WeaveTypeName] string typeName,
    [WeaveMethodName] string methodName,
    [WeaveLineNumber] int lineNumber,
    [WeaveFilePath] string filePath)
{
    Logger.Enter(typeName, methodName, lineNumber, filePath);
    WeaveTemplate.OriginalBody();
    Logger.Exit(typeName, methodName);
}
```

Set `genericWeave: true` on the attribute when matching methods or declaring types with open generic parameters:

```csharp
[Weave("MyApp.**.*Async(**)", priority: 5, genericWeave: true)]
public static void GenericWeave(
    object? instance,
    [WeaveTypeParams] Type[] genericTypeParams,
    [WeaveMethodName] string methodName)
{
    // ...
    WeaveTemplate.OriginalBody();
}
```

---

## Call-site weaving

`[WeaveCallSite]` searches the woven assembly for `call` and `callvirt` instructions whose callee matches the target signature. The callee can live in another assembly; SharpWeaver rewrites the caller-side instruction sequence and leaves the callee assembly unchanged.

Call-site weaving is synchronous in v1. It does not rewrite compiler-generated async state machine call sites, and it currently skips value-type instance callees.

Typical uses:

- Wrapping framework or engine API calls for profiling.
- Mutating arguments before a third-party method is called.
- Replacing a non-void return value without modifying the callee assembly.

```csharp
[WeaveCallSite("ExternalSdk.Client.Submit(string)", priority: 0)]
public static void SubmitCallWeave(Client instance, ref string payload, ref bool returnValue)
{
    payload = Normalize(payload);
    WeaveTemplate.OriginalBody();
    returnValue = returnValue && Audit(payload);
}
```

---

## Async weaving

Async templates use `await WeaveTemplate.OriginalBodyAsync()` as the splice marker.

### Supported async-like return types

`[AsyncWeave]` targets must be compiler-generated async methods (`[AsyncStateMachine]`) whose return type is one of:

| Return type | Notes |
| :---------- | :---- |
| `Task` | BCL task |
| `Task<T>` | BCL task with result |
| `ValueTask` | BCL value task |
| `ValueTask<T>` | BCL value task with result |
| `GDTask` | [GDTask](https://www.nuget.org/packages/GDTask) / `GodotTask` namespace |
| `GDTask<T>` | GDTask with result |

The weave template always uses BCL `Task` / `AsyncTaskMethodBuilder` on the template side. SharpWeaver rewrites those references to match the target return type and its async method builder when splicing into the state machine `MoveNext`.

### How splicing works

The weaver:

1. Locates the marker in both the template and target `MoveNext` methods.
2. Inserts template prefix IL before the target's initial work region.
3. Inserts template postfix IL after the target's completion path.
4. Rewrites `Task` / `AsyncTaskMethodBuilder` references to match the target async return type.

Template hoisted fields (except `<>u__*` await slots) are merged into the target state machine with a `<>w_` prefix.

> **Note:** Async weaving modifies state machine IL and exception handler boundaries. If you hit runtime `InvalidProgramException` after weaving, see [Troubleshooting](#troubleshooting).

---

## Override matching

| Mode | Behavior |
| :--- | :------- |
| **Exact** | Resolves the declared signature, then splices into **overrides** in the woven assembly ([VirtualOverrideWeave.cs](SharpWeaver.Examples/VirtualOverrideWeave.cs)) |
| **Wildcard** | Matches methods directly — no override inheritance walk |

---

## Weaving shenanigans

SharpWeaver runs **after** the C# compiler. It splices real IL (`call` / `callvirt`, branches, exception handlers) around target bodies. That is lower-level than normal compilation: the compiler's assumptions about initialization order, definite assignment, and "this helper only runs when you expect it" **do not automatically apply** to code you inject through weave templates.

### You are hooking arbitrary execution points

A weave prefix/postfix runs whenever the **target method** runs — including paths that would never have called your helper in hand-written code (static constructors, module initializers, first-touch static methods, property accessors, framework callbacks).

Anything your template calls from prefix/postfix executes at that moment, with whatever types and static state happen to be initialized **so far**.

### Static and module initialization

The CLR initializes types lazily (typically on first use), unless you are in an explicit init path:

| Init path | Why it matters for weaving |
| :-------- | :--------------------------- |
| Static constructor (`.cctor`) | Woven prefix runs **before** the target's original body. If the prefix touches another type, that type's `.cctor` may not have run yet. |
| `[ModuleInitializer]` | Runs once per assembly during load, in a fragile window. Extra calls from woven prefix/postfix can pull in dependencies before their module/type init is complete. |
| Static field initializers | Run as part of `.cctor`. Weaving a method that runs **before** another type's `.cctor` means that type's static fields may still be default (`null`, `0`). |
| `BeforeFieldInit` types | Static fields are not guaranteed initialized until the type is first accessed in a way that triggers init — easy to get wrong from woven early entry points. |

**Typical failure mode:** you weave a method that can run very early; the prefix calls `Helper.Record(...)` or resolves a singleton; `Helper`'s static constructor has not run yet → `NullReferenceException`, empty caches, double init, or subtle ordering bugs that only appear on cold start / Release / AOT.

```csharp
// Risky: SomeTarget.Early() may be the first code path that runs in the assembly.
[Weave("MyApp.SomeTarget.Early(**)", priority: 0)]
public static void EarlyWeave(object? instance)
{
    // Helper's .cctor might not have executed yet.
    Helper.Record("enter");
    WeaveTemplate.OriginalBody();
}
```

The same applies when the **target itself** is a static constructor, module initializer, or method called directly from one — you are adding work to the narrowest, least-forgiving part of the load graph.

### What tends to break

- `NullReferenceException` or invalid state in "impossible" places
- Logging / DI / config accessed before bootstrap finished
- Double initialization or missed one-time setup
- Deadlocks when prefix re-enters the same woven surface (see also broad wildcards in [Troubleshooting](#troubleshooting))

### Mitigations

- Treat prefix/postfix as **cold-start safe**: lazy-init helpers, null-tolerant guards, no assumptions that app singletons exist.
- Avoid weaving static constructors, module initializers, and other assembly-load entry points unless you have mapped the full init order.
- Do not use "whoever runs first" woven methods as your app bootstrap — use an explicit startup path.
- Keep wildcards narrow; exclude `*ctor`, accessors, and infrastructure you do not own (see [Weave method contracts](#weave-method-contracts)).
- Prefer idempotent, lazy helpers (`Lazy<T>`, first-touch init inside the helper method) over static fields on helper types that must be ready immediately.

---

## MSBuild integration

| Property | Default | Description |
| :------- | :------ | :---------- |
| `SharpWeaverEnabled` | `true` in Debug, else `false` | Run weaver after compile |
| `SharpWeaverForceRun` | `false` | Skip incremental inputs; weave every build |
| `SharpWeaverToolPath` | *(built tool)* | Override weaver DLL path |

Common commands:

```powershell
# Force weave (useful when incremental build skips weaving)
dotnet build -p:SharpWeaverForceRun=true

# Run weave target only
dotnet build -t:SharpWeave

# Disable weaving
dotnet build -p:SharpWeaverEnabled=false
```

### Incremental build sidecars

Do not delete manually:

| File | Purpose |
| :--- | :------ |
| `$(IntermediateOutputPath)$(TargetName).sharpweaver.stamp` | Output stamp — prevents MSBuild from skipping the weave step after `CoreCompile` |
| `$(IntermediateOutputPath)$(TargetName).sharpweaver.inputs` | Input fingerprint — changes to `DefineConstants` and similar trigger re-weave |

Release builds disable weaving by default. Async weaving on AOT/trimmed Release builds has not been fully validated.

---

## CLI

Run the weaver directly (without MSBuild):

```text
dotnet exec SharpWeaver.dll --assembly MyApp.dll --references "Ref1.dll;Ref2.dll"
dotnet exec SharpWeaver.dll --assembly MyApp.dll --dry-run --verbose
```

| Option | Description |
| :----- | :---------- |
| `--assembly` | Path to the DLL to weave (required) |
| `--references` | Semicolon-separated reference assembly paths |
| `--dry-run` | Plan and validate without writing output |
| `--verbose` | Detailed logging |

---

## Troubleshooting

| Symptom | What to check |
| :------ | :------------ |
| Exact weave matches nothing | Exact mode requires an override in the same assembly, or use wildcard `(**)` — see [Examples](#examples) |
| Build error: missing priority | Every `[Weave]` / `[AsyncWeave]` / `[WeaveCallSite]` must include `priority: N` |
| Build error: invalid wildcard | `**` cannot appear alone as a type or method name; parameter slots cannot use character globs |
| Stack overflow at runtime | Wildcard too broad; add `[WeaveExclude]`; exclude constructors, property accessors, and weave helper types |
| Line number / file path always `0` / `""` | PDB not available or no sequence points — capture falls back gracefully |
| `InvalidOperationException` from `WeaveTemplate.*` at runtime | Assembly was not woven — build with `SharpWeaverEnabled=true` or run the CLI weaver |
| Weave skipped on incremental build | Run with `-p:SharpWeaverForceRun=true`; verify `.sharpweaver.stamp` sidecar exists |
| Cecil PDB error during weave | Stale/corrupt PDB from a prior partial weave — clean and rebuild; weaver may delete corrupt PDB and rewrite without symbols |
| Runtime `InvalidProgramException` after async weave | Often short-branch overflow after IL insertion — ensure you are on a current SharpWeaver build; report with a minimal repro |
| Async prefix/postfix not executing | Verify template uses exactly one `await WeaveTemplate.OriginalBodyAsync()`; check wildcard matches the async state machine target |

---

## Tests

```powershell
dotnet test SharpWeaver.Tests/Unit/SharpWeaver.Tests.csproj
```

Projects under [`SharpWeaver.Tests/`](SharpWeaver.Tests/) cover sync/async weaving, wildcards, validation, override matching, generic weaving, and runtime behavior:

| Project | Role |
| :------ | :--- |
| `SharpWeaver.Tests/Unit` | xUnit test suite |
| `SharpWeaver.Tests/Fixtures` | Primary weave templates and target types |
| `SharpWeaver.Tests/BadSignature` | Invalid signature validation fixtures |
| `SharpWeaver.Tests/DuplicatePrefix` | Multi-weave onion composition fixtures |
| `SharpWeaver.Tests/InstancePatch` | Instance-patch scenario fixtures |

---

## License

MIT — see [LICENSE](LICENSE).

---

<br>

<a id="zh-hans"></a>

# SharpWeaver

> [English](#en-us) | **中文**

[![许可证](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

面向 .NET 的编译期 IL 织入器。用 `[Weave]`、`[AsyncWeave]` 与 `[WeaveCallSite]` 编写普通 C# 织入模板；SharpWeaver 在 `CoreCompile` 之后借助 [Mono.Cecil](https://github.com/jbevain/cecil) 将其拼接到目标方法。

| | |
| --- | --- |
| **基于特性的织入** | 用纯 C# 表达前缀、后缀、异常处理与控制流，无需手写 IL |
| **签名匹配** | 精确 CLR 签名与结构化通配符，可匹配单个方法或整组方法 |
| **同步与异步** | 同步用 `WeaveTemplate.OriginalBody()`；async 用 `await WeaveTemplate.OriginalBodyAsync()` 织入状态机 `MoveNext` |
| **调用点织入** | 在不改写 callee 程序集的前提下，包裹对外部程序集方法的调用 |
| **MSBuild 集成** | Debug 构建默认在编译后自动运行；增量构建依赖 stamp 辅助文件 |
| **元数据捕获** | 织入时从 PDB 注入类型名、方法名、行号与文件路径 |

---

## 目录

**概览**

- [摘要](#摘要)
- [包](#包)

**入门**

- [安装](#安装)
- [基本 API 用法](#基本-api-用法)
- [示例](#示例)

**织入模型**

- [签名模式](#签名模式)
- [织入方法约定](#织入方法约定)
- [优先级与分层](#优先级与分层)
- [通配符织入](#通配符织入)
- [调用点织入](#调用点织入)
- [Async 织入](#async-织入)
- [Override 匹配](#override-匹配)
- [织入陷阱](#织入陷阱)

**构建与运维**

- [MSBuild 集成](#msbuild-集成)
- [CLI](#cli)
- [故障排除](#故障排除)
- [测试](#测试)
- [许可证](#许可证)

---

## 摘要

SharpWeaver 是面向 .NET 程序集的后编译 IL 织入器。你定义**织入模板**——带 `[Weave]` 或 `[AsyncWeave]` 的普通 `static` 方法——并在模板中调用 `WeaveTemplate.OriginalBody()`（或 `await WeaveTemplate.OriginalBodyAsync()`）标记原目标方法体的内联位置。

构建时，SharpWeaver 会：

1. 扫描已编译程序集，收集织入模板及其目标签名。
2. 按精确 CLR 签名或结构化通配符模式匹配目标方法。
3. 将模板前缀/后缀 IL 拼接到各目标原方法体的内联副本两侧。
4. 重写分支、异常处理块，以及（async 场景下）状态机 `MoveNext` 的 IL，使结果合法且可运行。

模板本身就是 C#，因此可以直接使用 `try/catch`、提前返回、`using`、循环与 `await`；织入器负责 IL 重定位与类型重写。

---

## 包

| 项目 | 作用 |
| :--- | :--- |
| `SharpWeaver.Attributes` | `[Weave]`、`[AsyncWeave]`、`[WeaveCallSite]`、捕获属性、`WeaveTemplate` 标记 |
| `SharpWeaver.Build` | MSBuild 目标（编译后执行 `SharpWeave`） |
| `SharpWeaver` | CLI 织入器（`dotnet exec SharpWeaver.dll`） |
| `SharpWeaver.Examples` | 可运行示例手册（cookbook）——每个源文件一个场景 |

---

## 安装

在 `.csproj` 中引用 Attributes 项目并导入 MSBuild targets：

```xml
<ItemGroup>
  <ProjectReference Include="path\to\SharpWeaver.Attributes\SharpWeaver.Attributes.csproj" />
</ItemGroup>

<Import Project="path\to\SharpWeaver.Build\build\SharpWeaver.targets"
        Condition="Exists('path\to\SharpWeaver.Build\build\SharpWeaver.targets')" />
```

使用 **Debug** 配置构建（默认启用织入），或传入 `-p:SharpWeaverEnabled=true`。

NuGet 包将在后续版本提供；在此之前请使用上述项目引用。

---

## 基本 API 用法

在与待织入方法**同一程序集**中定义织入模板。

### 前缀 / 后缀

```csharp
using SharpWeaver;

public static class MyPatches
{
    [Weave("MyApp.Services.MyService.DoWork(int)", priority: 0)]
    public static void DoWorkWeave(MyService instance, ref int value)
    {
        Console.WriteLine("before");
        WeaveTemplate.OriginalBody();
        Console.WriteLine("after");
    }
}
```

### 异常处理

```csharp
[Weave("Godot.Node._Process(double)", priority: 0)]
public static void ProcessWeave(Node instance, ref double delta)
{
    try
    {
        WeaveTemplate.OriginalBody();
    }
    catch (Exception ex)
    {
        PublishProcessException(ex, instance);
        throw;
    }
}
```

### 提前返回跳过

非 void 目标：末尾追加 `ref TReturn returnValue`，可在 `OriginalBody()` 前 return：

```csharp
[Weave("MyApp.Counter.GetValue()", priority: 0)]
public static void GetValueWeave(Counter instance, ref int returnValue)
{
    if (ShouldSkip())
    {
        returnValue = 42;
        return;
    }

    WeaveTemplate.OriginalBody();
}
```

### Async 织入

模板侧使用 BCL `Task`；织入器会把 IL 重写为目标 async 返回类型（见 [Async 织入](#async-织入)）：

```csharp
[AsyncWeave("MyApp.Services.**.*Async(**)", priority: 0)]
public static async Task ServiceAsyncWeave(
    object? instance,
    [WeaveTypeName] string typeName,
    [WeaveMethodName] string methodName)
{
    LogEnter(typeName, methodName);
    await WeaveTemplate.OriginalBodyAsync();
    LogExit(typeName, methodName);
}
```

更多示例见 **[SharpWeaver.Examples](#示例)** 与[单元测试 / 测试辅助代码](SharpWeaver.Tests/)（`ValidPatches.cs`、`ValidAsyncPatches.cs`）。

---

## 示例

[`SharpWeaver.Examples`](SharpWeaver.Examples/) 是可运行的示例手册。**每个文件同时包含用户代码（目标类型/方法）与织入补丁**，演示一个场景。

构建并运行（Debug 下默认启用织入）：

```powershell
dotnet run --project SharpWeaver.Examples/SharpWeaver.Examples.csproj
```

| 文件 | 说明 |
| :--- | :--- |
| [PrefixPostfixLogging.cs](SharpWeaver.Examples/PrefixPostfixLogging.cs) | 带前缀/后缀日志的通配符同步织入 |
| [ExceptionWrap.cs](SharpWeaver.Examples/ExceptionWrap.cs) | 在 `WeaveTemplate.OriginalBody()` 周围使用 `try/catch` |
| [EarlyReturnSkip.cs](SharpWeaver.Examples/EarlyReturnSkip.cs) | 精确模式：在虚方法 override 上通过末尾 `ref TReturn returnValue` 跳过原方法体 |
| [WildcardInstrumentation.cs](SharpWeaver.Examples/WildcardInstrumentation.cs) | 通配符匹配 + `[WeaveTypeName]` / `[WeaveMethodName]` / PDB 捕获 |
| [WeaveExclude.cs](SharpWeaver.Examples/WeaveExclude.cs) | 使用 `[WeaveExclude]` 缩小通配符范围 |
| [MultiTargetWeave.cs](SharpWeaver.Examples/MultiTargetWeave.cs) | 同一补丁方法绑定多个 `[Weave]` |
| [PriorityLayering.cs](SharpWeaver.Examples/PriorityLayering.cs) | 同一目标叠加两层织入——按优先级层层包裹 |
| [StaticMethodWeave.cs](SharpWeaver.Examples/StaticMethodWeave.cs) | 静态方法通配符（`object? instance` → `ldnull`） |
| [VirtualOverrideWeave.cs](SharpWeaver.Examples/VirtualOverrideWeave.cs) | 基类虚方法的精确签名会织入 override |
| [AsyncTaskWeave.cs](SharpWeaver.Examples/AsyncTaskWeave.cs) | 对返回 `Task` 的 async 方法使用 `[AsyncWeave]` |
| [GenericWeaveCapture.cs](SharpWeaver.Examples/GenericWeaveCapture.cs) | `genericWeave: true` + `[WeaveTypeParams]` |

### 前缀/后缀日志

来自 [PrefixPostfixLogging.cs](SharpWeaver.Examples/PrefixPostfixLogging.cs)：

```csharp
namespace SharpWeaver.Examples.PrefixPostfixLogging;

public sealed class Greeter
{
    public void SayHello(string name) => Console.WriteLine($"Hello, {name}!");
}

public static class GreeterWeavePatch
{
    [Weave("SharpWeaver.Examples.PrefixPostfixLogging.Greeter.SayHello(**)", priority: 0)]
    public static void SayHelloWeave(object? instance)
    {
        Console.WriteLine("[enter] SayHello");
        WeaveTemplate.OriginalBody();
        Console.WriteLine("[leave] SayHello");
    }
}
```

### 异常包装

来自 [ExceptionWrap.cs](SharpWeaver.Examples/ExceptionWrap.cs)：

```csharp
[Weave("SharpWeaver.Examples.ExceptionWrap.Calculator.Divide(**)", priority: 0)]
public static void DivideWeave(object? instance)
{
    try
    {
        WeaveTemplate.OriginalBody();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[error] Divide failed: {ex.Message}");
        throw;
    }
}
```

### 提前返回跳过（精确模式 + override）

精确签名会解析**被织入程序集中的 override**，而不是仅命中声明处且无 override 的方法。见 [EarlyReturnSkip.cs](SharpWeaver.Examples/EarlyReturnSkip.cs)：

```csharp
public class CounterBase
{
    public virtual int GetNext() => 1;
}

public sealed class Counter : CounterBase
{
    private int _value;
    public override int GetNext() => ++_value;
}

[Weave("SharpWeaver.Examples.EarlyReturnSkip.CounterBase.GetNext()", priority: 0)]
public static void GetNextWeave(CounterBase instance, ref int returnValue)
{
    if (CounterWeavePatch.CachedValue is { } cached)
    {
        returnValue = cached;
        return;
    }

    WeaveTemplate.OriginalBody();
}
```

### 通配符追踪 + 捕获

来自 [WildcardInstrumentation.cs](SharpWeaver.Examples/WildcardInstrumentation.cs)：

```csharp
[Weave("SharpWeaver.Examples.WildcardInstrumentation.*.*(int)", priority: 5)]
public static void InstrumentWeave(
    object? instance,
    [WeaveTypeName] string typeName,
    [WeaveMethodName] string methodName,
    [WeaveLineNumber] int lineNumber,
    [WeaveFilePath] string filePath)
{
    Console.WriteLine($"[trace] {typeName}.{methodName} @ {filePath}:{lineNumber}");
    WeaveTemplate.OriginalBody();
}
```

> **提示：** 过宽的 `(**)` 通配符也会匹配构造函数与属性访问器。优先收窄参数模式（如上），或添加 `[WeaveExclude]` 规则。

### 优先级洋葱（两个织入，一个目标）

来自 [PriorityLayering.cs](SharpWeaver.Examples/PriorityLayering.cs)。运行时顺序：**外层 prefix → 内层 prefix → 方法体 → 内层 postfix → 外层 postfix**。

```csharp
[Weave("...LayeredService.Execute(**)", priority: 0)]   // 内层 — 先织入
public static void InnerWeave(object? instance) { /* ... */ }

[Weave("...LayeredService.Execute(**)", priority: 10)]  // 外层 — 后织入，包在最外
public static void OuterWeave(object? instance) { /* ... */ }
```

### Async Task 织入

来自 [AsyncTaskWeave.cs](SharpWeaver.Examples/AsyncTaskWeave.cs)：

```csharp
[AsyncWeave("SharpWeaver.Examples.AsyncTaskWeave.AsyncWorker.RunAsync(**)", priority: 0)]
public static async Task RunAsyncWeave(object? instance)
{
    Console.WriteLine("[async] prefix");
    await WeaveTemplate.OriginalBodyAsync();
    Console.WriteLine("[async] postfix");
}
```

### 调用点织入

当你要匹配的是每条 `call` / `callvirt` 指令的**被调用方法**，而不是要改写的 callee 方法体时，使用 `[WeaveCallSite]`。这适合在自己的程序集里包裹框架或第三方方法：

```csharp
[WeaveCallSite("ThirdParty.Api.Client.Send(string)", priority: 0)]
public static void SendCallWeave(Client instance, ref string message)
{
    Console.WriteLine("[before send]");
    WeaveTemplate.OriginalBody();
    Console.WriteLine("[after send]");
}
```

更多示例见 **[SharpWeaver.Examples](#示例)** 以及 [unit tests / fixtures](SharpWeaver.Tests/)（`ValidPatches.cs`、`ValidAsyncPatches.cs`、`CallSitePatches.cs`）。

---

## 签名模式

目标签名形如 `FullTypeName.MethodName(ParamTypes...)`，与 GodotSharp 风格的 XML 文档名一致。

### 精确模式

模式中不含 `*` 或 `**`。匹配单个 CLR 方法签名。

示例：`Godot.Node._Process(double)`

精确模式下，SharpWeaver 先解析声明签名，再**织入同一程序集中匹配的 override**。若程序集内不存在 override，则不会改写到声明方法本身——任意实例/静态方法请用通配符模式，或将精确模式与「虚基类 + override」配合（见 [EarlyReturnSkip.cs](SharpWeaver.Examples/EarlyReturnSkip.cs)）。

### 通配符模式

包含 `*` 或 `**`。对程序集中可织入的方法执行结构化匹配。

| 区域 | 标记 | 含义 |
| :--- | :--- | :--- |
| 命名空间段 | 字面量 | 精确段匹配 |
| 命名空间段 | `*` | 恰好一个段 |
| 命名空间段 | `**` | 零个或多个段 |
| 命名空间段 | `*text` / `text*` | 段内字符级 glob |
| 类型/方法名 | `*` | 任意名称 |
| 类型/方法名 | glob | 字符级 glob（类型/方法名不能仅为 `**`） |
| 参数 | 字面量 | 精确类型（与精确模式相同的别名） |
| 参数 | `*` | 恰好一个参数类型 |
| 参数 | `**` | 零个或多个参数类型；单独 `(**)` 匹配所有重载 |

无效通配符语法会在扫描阶段报错并导致构建失败。

示例：

| 模式 | 匹配 |
| :--- | :--- |
| `MyApp.Services.**.*.*(**)` | `MyApp.Services` 下及嵌套命名空间中的任何类型上的任何方法 |
| `MyApp.**.*Async(**)` | 名称以 `Async` 结尾的方法 |
| `MyApp.MyType.DoWork(int)` | 精确方法（与精确模式相同） |

---

## 织入方法约定

### 同步 `[Weave]`

| 模式 | 要求 |
| :--- | :--- |
| **精确** | `static void`；首参为目标实例类型（按值）；其余参数与目标参数一一对应。非 void 目标可在末尾追加 `ref TReturn returnValue` 以支持提前返回跳过。恰好一处 `WeaveTemplate.OriginalBody()`。 |
| **通配符** | 可选首参 `object? instance`（静态目标处变为 `ldnull`）。零个或多个捕获参数（每种至多一次）。无 per-target `ref` 参数；无 `ref TReturn`。恰好一处 `WeaveTemplate.OriginalBody()`。 |

编译器生成的 async 方法（`[AsyncStateMachine]`）不参与同步织入——请改用 `[AsyncWeave]`。

同步返回 async-like 类型的方法（见 [支持的 async-like 返回类型](#支持的-async-like-返回类型)）可用 `[WeaveExcludeAsyncLikeReturn]` 排除。

### Async `[AsyncWeave]`

- 模板侧为 `static async Task` 或 `static async Task<T>`。
- 模板体中恰好一处 `await WeaveTemplate.OriginalBodyAsync()`。
- 目标必须是编译器生成的 async 方法，且返回 async-like 类型。
- 织入发生在状态机 `MoveNext` 上；元数据捕获读取外层 async 方法。
- 通配符捕获参数规则与同步通配符织入相同。

未生成状态机、直接 `return Task.CompletedTask` 等返回 `Task`/`ValueTask` 的同步方法，仍按同步织入处理。

### 调用点 `[WeaveCallSite]`

- `static void`；模板必须是同步方法。
- 目标签名匹配的是被调用的方法，而不是被改写的方法体。
- `WeaveTemplate.OriginalBody()` 可出现 0 或 1 次。执行到标记时会在该处发出原始调用；在标记前 `return` 会跳过原始调用。
- 参数按调用栈顺序映射：实例调用先是 receiver，再是 callee 参数。参数可写成 `ref T` 以修改传给原始调用的值。
- 非 void callee 可在完整 receiver/参数列表后追加 `ref TReturn returnValue`，用于替换返回值。
- v1 会跳过值类型实例 callee，因为这类调用的隐藏 receiver 在 IL 中是 managed address；未来若要支持，需要专门的 byref receiver slot。

### 捕获属性

用于通配符织入参数；SharpWeaver 在 splice 时注入值：

| 属性 | 注入的值 |
| :--- | :------- |
| `[WeaveTypeName]` | `DeclaringType.FullName` |
| `[WeaveMethodName]` | 方法名称 |
| `[WeaveLineNumber]` | 第一个非隐藏 PDB 序列点行号，或 `0` |
| `[WeaveFilePath]` | 第一个 PDB 文档路径，或 `""` |
| `[WeaveTypeParams]` | 目标方法上的开放泛型参数 `Type[]` |

### 排除

| 属性 | 用途 |
| :--- | :--- |
| `[WeaveExclude(excludedSignature)]` | 从该织入方法的通配符枚举中剔除匹配目标（语法与精确/通配符模式相同） |
| `[WeaveExcludeAsyncLikeReturn]` | 跳过返回 async-like 类型的同步目标 |

过宽的通配符应排除 `*ctor`、`get_*`、`set_*` 以及补丁/辅助类型，避免递归与无意义的代码注入。

通配符模式还会自动排除：

- 继承 `System.Attribute` 的类型上的全部方法（避免反射读特性时递归）。
- 被织入程序集中、由织入模板体直接引用的 callee。

### 可织入方法过滤器

| | |
| --- | --- |
| **包含** | 有 IL 体的方法；非 `abstract`、`extern` 或 P/Invoke |
| **排除** | 织入模板方法本身；编译器生成名（`<`、`>`、`__` 前缀） |

---

## 优先级与分层

`priority` 是 `[Weave]`、`[AsyncWeave]` 与 `[WeaveCallSite]` 构造函数的**必填**参数。

> **运行时顺序：** 外层 prefix → 内层 prefix → 方法体 → 内层 postfix → 外层 postfix

- 组合时**按 priority 升序**应用（数值越小越先织入）。
- 较低 priority **更靠近原方法体**（内层）；较高 priority 后织入并包在**外层**。
- 运行时 prefix：**外层 → 内层 → 方法体**；postfix：**内层 → 外层**。
- 相同 priority：按发现顺序。
- 一个目标可匹配多个织入；一个织入方法可带多个特性（`AllowMultiple = true`）。

```csharp
[Weave("MyApp.**.*(**)", priority: 0)]   // 内层 — 更靠近原方法体
public static void InnerWeave(object? instance) { /* ... */ WeaveTemplate.OriginalBody(); }

[Weave("MyApp.**.*(**)", priority: 10)]  // 外层 — 更高 priority，包在最外
public static void OuterWeave(object? instance) { /* ... */ WeaveTemplate.OriginalBody(); }
```

---

## 通配符织入

带元数据捕获的完整检测示例：

```csharp
[Weave("MyApp.Services.**.*.*(**)", priority: 10)]
[WeaveExclude("MyApp.Services.Internal.*.*(**)")]
public static void InstrumentWeave(
    object? instance,
    [WeaveTypeName] string typeName,
    [WeaveMethodName] string methodName,
    [WeaveLineNumber] int lineNumber,
    [WeaveFilePath] string filePath)
{
    Logger.Enter(typeName, methodName, lineNumber, filePath);
    WeaveTemplate.OriginalBody();
    Logger.Exit(typeName, methodName);
}
```

当匹配带有开放泛型参数的方法或声明类型时，在属性上设置 `genericWeave: true`：

```csharp
[Weave("MyApp.**.*Async(**)", priority: 5, genericWeave: true)]
public static void GenericWeave(
    object? instance,
    [WeaveTypeParams] Type[] genericTypeParams,
    [WeaveMethodName] string methodName)
{
    // ...
    WeaveTemplate.OriginalBody();
}
```

---

## 调用点织入

`[WeaveCallSite]` 会在被织入程序集中查找 callee 匹配目标签名的 `call` / `callvirt` 指令。callee 可以位于另一个程序集；SharpWeaver 只改写 caller 侧的指令序列，不修改 callee 程序集。

v1 的调用点织入仅支持同步调用点。它不会改写编译器生成的 async 状态机调用点，并且当前会跳过值类型实例 callee。

典型用途：

- 为框架或引擎 API 调用添加 profiling。
- 在调用第三方方法前修改参数。
- 在不改写 callee 程序集的前提下替换非 void 返回值。

```csharp
[WeaveCallSite("ExternalSdk.Client.Submit(string)", priority: 0)]
public static void SubmitCallWeave(Client instance, ref string payload, ref bool returnValue)
{
    payload = Normalize(payload);
    WeaveTemplate.OriginalBody();
    returnValue = returnValue && Audit(payload);
}
```

---

## Async 织入

Async 模板以 `await WeaveTemplate.OriginalBodyAsync()` 作为 splice 标记。

### 支持的 async-like 返回类型

`[AsyncWeave]` 的目标必须是编译器生成的 async 方法（`[AsyncStateMachine]`），且返回类型为下列之一：

| 返回类型 | 说明 |
| :------- | :--- |
| `Task` | BCL 任务 |
| `Task<T>` | 带结果的 BCL 任务 |
| `ValueTask` | BCL 值任务 |
| `ValueTask<T>` | 带结果的 BCL 值任务 |
| `GDTask` | [GDTask](https://www.nuget.org/packages/GDTask) / `GodotTask` 命名空间 |
| `GDTask<T>` | 带结果的 GDTask |

模板侧始终使用 BCL `Task` / `AsyncTaskMethodBuilder`；SharpWeaver splice 进状态机 `MoveNext` 时会重写这些引用，以匹配目标的返回类型与 async 方法构建器。

### Splice 流程

织入器会：

1. 在模板与目标 `MoveNext` 中定位标记。
2. 在目标初始工作区之前插入模板 prefix IL。
3. 在目标完成路径之后插入模板 postfix IL。
4. 将 `Task` / `AsyncTaskMethodBuilder` 引用重写为目标 async 返回类型。

模板 hoist 的字段（`<>u__*` await 槽除外）会以 `<>w_` 前缀合并进目标状态机。

> **注意：** Async 织入会改动状态机 IL 与异常处理边界。若织入后出现运行时 `InvalidProgramException`，见 [故障排除](#故障排除)。

---

## Override 匹配

| 模式 | 行为 |
| :--- | :--- |
| **精确** | 解析声明签名后，织入被织入程序集中该方法的 **override**（[VirtualOverrideWeave.cs](SharpWeaver.Examples/VirtualOverrideWeave.cs)） |
| **通配符** | 直接在程序集内匹配方法，不沿 override 继承链展开 |

---

## 织入陷阱

SharpWeaver 在 **C# 编译之后**运行，向目标方法体两侧 splice 真实 IL（`call` / `callvirt`、分支、异常处理）。这比常规编译更底层：编译器关于初始化顺序、明确赋值、以及「这段辅助代码只会在预期时机执行」的假设，**不会自动延伸**到你通过织入模板注入的代码。

### 你在 hook 任意执行点

织入 prefix/postfix 会在**目标方法每次执行时**运行——包括手写代码里本不会调用该 helper 的路径（静态构造函数、module initializer、首次触达的 static 方法、属性访问器、框架回调等）。

模板在 prefix/postfix 里调用的任何代码，都会在那个时刻执行，且只能依赖**当时已经完成**的类型与静态状态。

### 静态初始化与 module 初始化

CLR 对类型做惰性初始化（通常首次使用时），除非你处于明确的初始化路径：

| 初始化路径 | 与织入的关系 |
| :--------- | :----------- |
| 静态构造函数（`.cctor`） | Woven prefix 在目标原方法体**之前**运行。若 prefix 访问其他类型，该类型的 `.cctor` 可能尚未执行。 |
| `[ModuleInitializer]` | 程序集加载时执行一次，窗口极窄。prefix/postfix 额外引入的调用可能在其依赖的类型/module 初始化完成前触发。 |
| 静态字段初始化器 | 作为 `.cctor` 的一部分运行。若某方法在另一类型的 `.cctor` 之前被织入并执行，该类型的静态字段可能仍为默认值（`null`、`0`）。 |
| `BeforeFieldInit` 类型 | 静态字段直到触发类型 init 的访问才保证就绪——从过早的 woven 入口调用时很容易踩坑。 |

**常见失败模式：** 织入了一个可能极早执行的方法；prefix 调用 `Helper.Record(...)` 或解析单例；`Helper` 的静态构造函数尚未运行 → `NullReferenceException`、空缓存、重复初始化，或仅在冷启动 / Release / AOT 下出现的顺序问题。

```csharp
// 有风险：SomeTarget.Early() 可能是程序集里最先跑到的路径之一。
[Weave("MyApp.SomeTarget.Early(**)", priority: 0)]
public static void EarlyWeave(object? instance)
{
    // Helper 的 .cctor 可能还没执行。
    Helper.Record("enter");
    WeaveTemplate.OriginalBody();
}
```

当**目标本身**就是静态构造函数、module initializer，或由其直接调用的方法时，问题更严重——你在加载图最窄、最不容错的节点上增加了额外工作。

### 容易出问题的现象

- 在「不可能」的位置出现 `NullReferenceException` 或非法状态
- 在 bootstrap 完成前访问日志 / DI / 配置
- 重复初始化或漏掉一次性 setup
- prefix 重入同一 woven 面导致死锁（另见 [故障排除](#故障排除) 中的宽泛通配符）

### 缓解措施

- 把 prefix/postfix 当作**冷启动安全**代码：helper 惰性初始化、可空防护、不假设应用单例已存在。
- 除非已梳理完整 init 顺序，否则避免织入静态构造函数、module initializer 及其他程序集加载入口。
- 不要用「谁先跑到谁」的 woven 方法充当应用 bootstrap——使用显式启动路径。
- 收窄通配符；排除 `*ctor`、访问器与不拥有的基础设施（见 [织入方法约定](#织入方法约定)）。
- 优先使用幂等、惰性的 helper（`Lazy<T>`、在 helper 方法内 first-touch init），而非依赖「必须立即可用」的 helper 静态字段。

---

## MSBuild 集成

| 属性 | 默认值 | 描述 |
| :--- | :----- | :--- |
| `SharpWeaverEnabled` | Debug 为 `true`，否则 `false` | 编译后运行织入器 |
| `SharpWeaverForceRun` | `false` | 忽略增量输入；每次构建都织入 |
| `SharpWeaverToolPath` | *(内置工具)* | 覆盖织入器 DLL 路径 |

常用命令：

```powershell
# 强制织入（增量构建跳过织入时有用）
dotnet build -p:SharpWeaverForceRun=true

# 仅运行织入目标
dotnet build -t:SharpWeave

# 禁用织入
dotnet build -p:SharpWeaverEnabled=false
```

### 增量构建辅助文件

请勿手动删除：

| 文件 | 用途 |
| :--- | :--- |
| `$(IntermediateOutputPath)$(TargetName).sharpweaver.stamp` | 输出 stamp — 避免 MSBuild 在 `CoreCompile` 后跳过织入 |
| `$(IntermediateOutputPath)$(TargetName).sharpweaver.inputs` | 输入指纹 — `DefineConstants` 等变更时触发重新织入 |

Release 默认关闭织入。AOT/裁剪 Release 上的 async 织入尚未充分验证。

---

## CLI

不经过 MSBuild，直接运行织入器：

```text
dotnet exec SharpWeaver.dll --assembly MyApp.dll --references "Ref1.dll;Ref2.dll"
dotnet exec SharpWeaver.dll --assembly MyApp.dll --dry-run --verbose
```

| 选项 | 描述 |
| :--- | :--- |
| `--assembly` | 要织入的 DLL 路径（必需） |
| `--references` | 分号分隔的引用程序集路径 |
| `--dry-run` | 仅规划与校验，不写回输出 |
| `--verbose` | 输出详细日志 |

---

## 故障排除

| 症状 | 检查项 |
| :--- | :----- |
| 精确织入未命中任何方法 | 精确模式要求同一程序集内有 override，或改用通配符 `(**)` — 见 [示例](#示例) |
| 构建错误：缺少 priority | 每个 `[Weave]` / `[AsyncWeave]` / `[WeaveCallSite]` 都必须写 `priority: N` |
| 构建错误：无效通配符 | 类型/方法名不能仅为 `**`；参数位不支持字符级 glob |
| 运行时栈溢出 | 通配符过宽；添加 `[WeaveExclude]`；排除构造函数、属性访问器与织入辅助类型 |
| 行号 / 文件路径恒为 `0` / `""` | 无 PDB 或无 sequence point — 捕获值会回退为默认值 |
| 运行时 `WeaveTemplate.*` 抛出 `InvalidOperationException` | 程序集未织入 — 以 `SharpWeaverEnabled=true` 构建，或运行 CLI 织入器 |
| 增量构建跳过织入 | 使用 `-p:SharpWeaverForceRun=true`；确认存在 `.sharpweaver.stamp` |
| 织入时 Cecil PDB 报错 | 陈旧/损坏 PDB（常见于部分织入后）— 清理并重建；织入器可能删除坏 PDB 并无符号写回 |
| Async 织入后 `InvalidProgramException` | 多为 IL 插入导致短跳转溢出 — 请用最新 SharpWeaver 并提供最小复现 |
| Async prefix/postfix 未执行 | 确认模板仅有一处 `await WeaveTemplate.OriginalBodyAsync()`；检查通配符是否命中 async 状态机目标 |

---

## 测试

```powershell
dotnet test SharpWeaver.Tests/Unit/SharpWeaver.Tests.csproj
```

[`SharpWeaver.Tests/`](SharpWeaver.Tests/) 覆盖同步/async 织入、通配符、校验、override 匹配、泛型织入与运行时行为：

| 项目 | 作用 |
| :--- | :--- |
| `SharpWeaver.Tests/Unit` | xUnit 测试项目 |
| `SharpWeaver.Tests/Fixtures` | 主织入模板与目标类型 |
| `SharpWeaver.Tests/BadSignature` | 非法签名校验测试用例 |
| `SharpWeaver.Tests/DuplicatePrefix` | 多层织入组合测试用例 |
| `SharpWeaver.Tests/InstancePatch` | 实例 patch 场景测试用例 |

---

## 许可证

MIT — 参见 [LICENSE](LICENSE)。
