using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Diagnostics.CodeAnalysis;

namespace SharpWeaver;

/// <summary>Finds method call instructions matched by <see cref="WeaveCallSiteAttribute"/> templates.</summary>
public static class CallSiteWeaveMatcher
{
    /// <summary>Adds call-site matches for one call-site weave.</summary>
    /// <param name="weave">CallSite-code weave information.</param>
    /// <param name="resolver">Assembly resolver.</param>
    /// <param name="wovenModule">Target module to be woven.</param>
    /// <param name="matches">Caller method to call-site matches.</param>
    /// <param name="weaveTemplateCallees">Set of methods directly called by weave templates, to exclude from caller matching.</param>
    /// <param name="error">Error message on failure.</param>
    /// <returns>Returns <see langword="true"/> on success.</returns>
    public static bool TryMatch(
        WeaveInfo weave,
        IAssemblyResolver resolver,
        ModuleDefinition wovenModule,
        Dictionary<MethodDefinition, List<CallSiteWeaveMatch>> matches,
        IReadOnlySet<MethodDefinition> weaveTemplateCallees,
        out string? error)
    {
        error = null;
        MethodDefinition? exactTarget = null;
        WildcardSignaturePattern? wildcardTarget = null;

        switch (weave.Pattern)
        {
            case ExactSignaturePattern exact:
                if (!resolver.TryResolveMethod(exact.Parsed, out exactTarget, out var resolveError))
                {
                    error = $"WeaveCallSite 编织 '{weave.WeaveMethodDisplayName}'：{resolveError}";
                    return false;
                }

                break;

            case WildcardSignaturePatternWrapper wildcard:
                wildcardTarget = wildcard.Parsed;
                break;
        }

        foreach (var type in wovenModule.EnumerateAllTypes())
        {
            foreach (var callerMethod in type.Methods)
            {
                if (!WeaveMethodFilter.IsSyncWeaveCandidate(callerMethod, weave, weaveTemplateCallees))
                {
                    continue;
                }

                if (WeaveExclusionMatcher.IsExcluded(weave, callerMethod))
                {
                    continue;
                }

                foreach (var instruction in callerMethod.Body.Instructions)
                {
                    if (!IsSupportedCall(instruction))
                    {
                        continue;
                    }

                    if (instruction.Operand is not MethodReference calledMethod)
                    {
                        continue;
                    }

                    if (!TryResolveCalledMethod(calledMethod, out var resolvedCalledMethod))
                    {
                        continue;
                    }

                    if (HasUnsupportedValueTypeInstanceSlot(calledMethod, resolvedCalledMethod))
                    {
                        continue;
                    }

                    if (weave.GenericWeave != RequiresGenericWeave(calledMethod, resolvedCalledMethod))
                    {
                        continue;
                    }

                    var isMatch = exactTarget != null
                        ? IsSameMethod(resolvedCalledMethod, exactTarget)
                        : wildcardTarget != null && WildcardSignatureMatcher.IsMatch(wildcardTarget, resolvedCalledMethod);

                    if (!isMatch)
                    {
                        continue;
                    }

                    if (WeaveExclusionMatcher.IsExcluded(weave, resolvedCalledMethod))
                    {
                        continue;
                    }

                    AddMatch(matches, callerMethod, instruction, calledMethod, resolvedCalledMethod, weave);
                }
            }
        }

        return true;
    }

    private static bool IsSupportedCall(Instruction instruction) =>
        instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt;

    private static bool TryResolveCalledMethod(
        MethodReference calledMethod,
        [NotNullWhen(true)] out MethodDefinition? resolvedCalledMethod)
    {
        resolvedCalledMethod = null;
        try
        {
            resolvedCalledMethod = calledMethod.Resolve();
            return resolvedCalledMethod != null;
        }
        catch (AssemblyResolutionException)
        {
            return false;
        }
    }

    private static bool RequiresGenericWeave(MethodReference calledMethod, MethodDefinition resolvedCalledMethod) =>
        calledMethod is GenericInstanceMethod
        || resolvedCalledMethod.HasGenericParameters
        || WeaveMethodFilter.RequiresGenericWeave(resolvedCalledMethod);

    private static bool HasUnsupportedValueTypeInstanceSlot(
        MethodReference calledMethod,
        MethodDefinition resolvedCalledMethod) =>
        calledMethod.HasThis && IsValueType(resolvedCalledMethod.DeclaringType);

    private static bool IsValueType(TypeReference type)
    {
        while (type is TypeSpecification specification)
        {
            type = specification.ElementType;
        }

        if (type.IsValueType)
        {
            return true;
        }

        try
        {
            return type.Resolve()?.IsValueType == true;
        }
        catch (AssemblyResolutionException)
        {
            return false;
        }
    }

    private static bool IsSameMethod(MethodDefinition left, MethodDefinition right) =>
        left.FullName == right.FullName
        && left.DeclaringType.Scope.Name == right.DeclaringType.Scope.Name;

    private static void AddMatch(
        Dictionary<MethodDefinition, List<CallSiteWeaveMatch>> matches,
        MethodDefinition callerMethod,
        Instruction callInstruction,
        MethodReference calledMethod,
        MethodDefinition resolvedCalledMethod,
        WeaveInfo weave)
    {
        if (!matches.TryGetValue(callerMethod, out var callSites))
        {
            callSites = [];
            matches[callerMethod] = callSites;
        }

        var match = callSites.FirstOrDefault(existing => ReferenceEquals(existing.CallInstruction, callInstruction));
        if (match == null)
        {
            callSites.Add(new CallSiteWeaveMatch(callInstruction, calledMethod, resolvedCalledMethod, [weave]));
            return;
        }

        foreach (var existing in match.Weaves)
        {
            if (ReferenceEquals(existing.WeaveMethod, weave.WeaveMethod))
            {
                return;
            }
        }

        match.Weaves.Add(weave);
    }
}
