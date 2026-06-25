using System;

namespace SharpWeaver;

/// <summary>
/// Marks a SharpWeaver synchronous weave method. Injects the method body (including prefix and postfix code) into matching target overrides.
/// </summary>
/// <remarks>
/// <para>Weave method must be <c>static void</c>.</para>
/// <para>
/// Exact signature mode (without <c>*</c> / <c>**</c>): target is a single CLR method signature, e.g.
/// <c>Godot.Node._Process(double)</c>.
/// </para>
/// <para>
/// Wildcard mode (containing <c>*</c> or <c>**</c>): performs structured wildcard matching against weaveable methods in the assembly.
/// Namespace segments support <c>*</c> (single segment), <c>**</c> (zero or more segments), and character-level glob;
/// type name and method name support <c>*</c> and glob; parameter lists support <c>*</c> (single parameter) and <c>**</c> (zero or more parameters).
/// The weave method's first parameter may be <c>object? instance</c>; it may be followed by metadata parameters with capture attributes.
/// </para>
/// <para>
/// The method body calls <see cref="WeaveTemplate.OriginalBody"/> as a marker,
/// which SharpWeaver replaces with an inline copy of the target method's original body.
/// </para>
/// <para>
/// Compiler-generated async methods (with <c>[AsyncStateMachine]</c>) are not sync weave targets;
/// use <see cref="AsyncWeaveAttribute"/> instead.
/// </para>
/// <para>
/// A single weave method can be annotated with multiple <see cref="WeaveAttribute"/> (<c>AllowMultiple</c>),
/// each independently participating in target matching; a weave method is not applied twice to the same target.
/// </para>
/// <para>
/// A single target method can match multiple weaves; they are applied in ascending <see cref="Priority"/> order (smaller values are outer onion layers).
/// When priorities are equal, discovery order is used.
/// </para>
/// </remarks>
/// <param name="targetSignature">Exact CLR signature or wildcard pattern.</param>
/// <param name="priority">Weave priority (ascending order of application).</param>
/// <param name="genericWeave">Whether to match methods or declaring types with open generic parameters.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class WeaveAttribute(string targetSignature, int priority, bool genericWeave = false) : Attribute
{
    /// <summary>Target signature or wildcard pattern string.</summary>
    public string TargetSignature { get; } = targetSignature;

    /// <summary>Weave priority; smaller values are applied first (outer onion layer).</summary>
    public int Priority { get; } = priority;

    /// <summary>Whether to match methods or declaring types with open generic parameters.</summary>
    public bool GenericWeave { get; } = genericWeave;
}

/// <summary>
/// Marks a wildcard target exclusion pattern for a weave method, used to exclude specific methods from the matched target set.
/// </summary>
/// <remarks>
/// Can be used with <see cref="WeaveAttribute"/> or <see cref="AsyncWeaveAttribute"/>.
/// Exclusion signatures use the same exact or wildcard syntax as target signatures; currently only effective during wildcard target enumeration.
/// </remarks>
/// <param name="excludedSignature">Exact CLR signature or wildcard pattern to exclude from wildcard match results.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class WeaveExcludeAttribute(string excludedSignature) : Attribute
{
    /// <summary>Excluded target signature or wildcard pattern string.</summary>
    public string ExcludedSignature { get; } = excludedSignature;
}

/// <summary>
/// Marks a sync weave method to skip target methods that return async-like types (<c>Task</c>, <c>GDTask</c>, <c>ValueTask</c>, etc.).
/// </summary>
/// <remarks>
/// Compiler-generated async methods are always automatically excluded from sync weaving due to <c>[AsyncStateMachine]</c>;
/// this attribute is used to additionally exclude wrapper methods that synchronously return async-like objects.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class WeaveExcludeAsyncLikeReturnAttribute : Attribute;

/// <summary>
/// Marks a SharpWeaver synchronous call-site weave method. Replaces calls to a matching target method with template code.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="WeaveAttribute"/>, the target signature identifies the callee instruction to surround,
/// not the body of that method. This is useful for calls to methods declared in another assembly.
/// </para>
/// <para>
/// The weave method must be <c>static void</c>. Its body may contain one <see cref="WeaveTemplate.OriginalBody"/>
/// marker; when present and reached, SharpWeaver emits the original call at that point.
/// Control flow that returns before the marker skips the original call.
/// </para>
/// <para>
/// Parameters map positionally to the call instance (for instance calls) and callee arguments. Parameters may be
/// declared as <c>ref</c> to mutate the value passed to the original call. Non-void callees may append a trailing
/// <c>ref TReturn</c> return-value slot after all call arguments.
/// </para>
/// </remarks>
/// <param name="targetSignature">Exact CLR signature or wildcard pattern for the called method.</param>
/// <param name="priority">Weave priority for multiple call-site templates on the same call site.</param>
/// <param name="genericWeave">Whether to match methods or declaring types with open generic parameters.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class WeaveCallSiteAttribute(string targetSignature, int priority, bool genericWeave = false) : Attribute
{
    /// <summary>Target callee signature or wildcard pattern string.</summary>
    public string TargetSignature { get; } = targetSignature;

    /// <summary>Weave priority; smaller values are applied first around a call site.</summary>
    public int Priority { get; } = priority;

    /// <summary>Whether to match methods or declaring types with open generic parameters.</summary>
    public bool GenericWeave { get; } = genericWeave;
}

/// <summary>
/// Marks a SharpWeaver async weave method. Splices the async weave template into the target state machine's <c>MoveNext</c>.
/// </summary>
/// <remarks>
/// <para>Weave method must be <c>static async Task</c> or <c>static async Task&lt;T&gt;</c> (template side consistently uses BCL Task).</para>
/// <para>
/// Exact and wildcard signature modes are the same as <see cref="WeaveAttribute"/>;
/// the matching target must be a compiler-generated async method returning an async-like type (<c>GDTask</c>, <c>Task</c>, etc.).
/// </para>
/// <para>
/// <c>await WeaveTemplate.OriginalBodyAsync()</c> is used as a marker within the method body;
/// SharpWeaver inlines the original async method body into the target <c>MoveNext</c> and automatically
/// rewrites <c>Task</c> / <c>AsyncTaskMethodBuilder</c> to the target async type.
/// </para>
/// <para>
/// Wildcard mode: optional first parameter <c>object? instance</c>; followed by metadata parameters with capture attributes (same as sync weaving).
/// </para>
/// <para>
/// A single weave method can be annotated with multiple <see cref="AsyncWeaveAttribute"/> (<c>AllowMultiple</c>).
/// Priority rules are the same as <see cref="WeaveAttribute"/>.
/// </para>
/// </remarks>
/// <param name="targetSignature">Exact CLR signature or wildcard pattern.</param>
/// <param name="priority">Weave priority (ascending order of application).</param>
/// <param name="genericWeave">Whether to match methods or declaring types with open generic parameters.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class AsyncWeaveAttribute(string targetSignature, int priority, bool genericWeave = false) : Attribute
{
    /// <summary>Target signature or wildcard pattern string.</summary>
    public string TargetSignature { get; } = targetSignature;

    /// <summary>Weave priority; smaller values are applied first (outer onion layer).</summary>
    public int Priority { get; } = priority;

    /// <summary>Whether to match methods or declaring types with open generic parameters.</summary>
    public bool GenericWeave { get; } = genericWeave;
}

/// <summary>
/// Marks a weave method parameter to be injected with the target method name by SharpWeaver during splicing.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class WeaveMethodNameAttribute : Attribute;

/// <summary>
/// Marks a weave method parameter to be injected with the target's declaring type name by SharpWeaver during splicing.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class WeaveTypeNameAttribute : Attribute;

/// <summary>
/// Marks a weave method parameter to be injected with the open generic parameter <see cref="Type"/> array visible on the target method.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class WeaveTypeParamsAttribute : Attribute;

/// <summary>
/// Marks a weave method parameter to be injected with the line number of the first sequence point in the target method's PDB; <c>0</c> when no PDB is available.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class WeaveLineNumberAttribute : Attribute;

/// <summary>
/// Marks a weave method parameter to be injected with the document path of the first sequence point in the target method's PDB; <c>""</c> when no PDB is available.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class WeaveFilePathAttribute : Attribute;
