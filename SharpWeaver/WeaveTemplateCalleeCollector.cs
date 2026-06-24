using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver;

/// <summary>
/// Collects direct call targets within the target assembly from weave template method bodies (and async template <c>MoveNext</c>),
/// for exclusion during wildcard matching to avoid self-references or recursion when weaving infrastructure code.
/// </summary>
public static class WeaveTemplateCalleeCollector
{
    /// <summary>
    /// Collects all methods directly <c>call</c>/<c>callvirt</c>/<c>newobj</c>ed by weave templates within the target module.
    /// </summary>
    /// <param name="weaves">Scanned weave method list.</param>
    /// <param name="wovenModule">Target module to be woven.</param>
    /// <returns>Set of methods to exclude from wildcard matching.</returns>
    public static HashSet<MethodDefinition> Collect(
        IReadOnlyList<WeaveInfo> weaves,
        ModuleDefinition wovenModule)
    {
        var excluded = new HashSet<MethodDefinition>();
        foreach (var weave in weaves)
        {
            CollectFromWeaveMethod(weave.WeaveMethod, wovenModule, excluded);
            if (weave.IsAsync
                && AsyncMethodHelper.TryResolveMoveNext(weave.WeaveMethod, out var moveNext, out _))
            {
                CollectFromWeaveMethod(moveNext, wovenModule, excluded);
            }
        }

        return excluded;
    }

    private static void CollectFromWeaveMethod(
        MethodDefinition weaveMethod,
        ModuleDefinition wovenModule,
        HashSet<MethodDefinition> excluded)
    {
        if (!weaveMethod.HasBody)
        {
            return;
        }

        foreach (var instruction in weaveMethod.Body.Instructions)
        {
            if (!IsMethodInvoke(instruction))
            {
                continue;
            }

            if (instruction.Operand is not MethodReference methodReference)
            {
                continue;
            }

            if (IsOriginalBodyMarker(methodReference))
            {
                continue;
            }

            if (!TryResolveInWovenModule(methodReference, wovenModule, out var resolvedMethod))
            {
                continue;
            }

            excluded.Add(resolvedMethod);
        }
    }

    private static bool IsMethodInvoke(Instruction instruction) =>
        instruction.OpCode == OpCodes.Call
        || instruction.OpCode == OpCodes.Callvirt
        || instruction.OpCode == OpCodes.Calli
        || instruction.OpCode == OpCodes.Newobj;

    private static bool IsOriginalBodyMarker(MethodReference methodReference) =>
        methodReference.DeclaringType.FullName == SharpWeaverMetadata.WeaveTemplate
        && methodReference.Name is SharpWeaverMetadata.OriginalBodyMethod or SharpWeaverMetadata.OriginalBodyAsyncMethod;

    private static bool TryResolveInWovenModule(
        MethodReference methodReference,
        ModuleDefinition wovenModule,
        out MethodDefinition resolvedMethod)
    {
        resolvedMethod = null!;
        try
        {
            var resolved = methodReference.Resolve();
            if (resolved.Module != wovenModule)
            {
                return false;
            }

            resolvedMethod = resolved;
            return true;
        }
        catch (AssemblyResolutionException)
        {
            return false;
        }
    }
}
