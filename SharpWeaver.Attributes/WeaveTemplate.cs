using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SharpWeaver;

/// <summary>
/// SharpWeaver weave template helper class providing marker calls within weave methods.
/// </summary>
public static class WeaveTemplate
{
    /// <summary>
    /// Original method body call marker. SharpWeaver replaces this call with an inline copy of the target's original method body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method acts as a position marker within a weave method (annotated with <see cref="WeaveAttribute"/>):
    /// code before the marker serves as the prefix, code after the marker serves as the postfix,
    /// and the marker itself is replaced with an inline copy of the original method body (all <c>ret</c> instructions are rewritten to jump to the postfix entry point).
    /// </para>
    /// <para>
    /// If called directly at runtime (i.e., the assembly has not been processed by SharpWeaver), a <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// <para>
    /// <c>[MethodImpl(MethodImplOptions.NoInlining)]</c> ensures the compiler does not inline this call away,
    /// so that Cecil can reliably locate the <c>call</c> instruction as a marker within the method body.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void OriginalBody()
    {
        throw new InvalidOperationException(
            "WeaveTemplate.OriginalBody() 在运行时被直接调用。" +
            "该方法仅作为 SharpWeaver 的位置标记，程序集必须经过编织后方可正常运行。");
    }

    /// <summary>
    /// Async original method body call marker. SharpWeaver replaces the <c>await</c> of this call
    /// within the target state machine <c>MoveNext</c> with an inline copy of the target async method body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used for weave templates annotated with <see cref="AsyncWeaveAttribute"/>;
    /// the template side consistently uses <c>Task</c>, and the weaver automatically rewrites it to the target async return type (e.g., <c>GDTask</c>).
    /// </para>
    /// <para>
    /// If called directly at runtime (i.e., the assembly has not been processed by SharpWeaver), a <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task OriginalBodyAsync()
    {
        await Task.CompletedTask;
        throw new InvalidOperationException(
            "WeaveTemplate.OriginalBodyAsync() 在运行时被直接调用。" +
            "该方法仅作为 SharpWeaver 的位置标记，程序集必须经过编织后方可正常运行。");
    }
}
