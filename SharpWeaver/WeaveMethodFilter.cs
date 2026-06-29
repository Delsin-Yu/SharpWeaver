using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Determines whether methods in the target assembly are eligible as ILWeaving targets.</summary>
public static class WeaveMethodFilter
{
    private const string SystemAttributeFullName = "System.Attribute";

    /// <summary>Whether the method has a weaveable IL method body.</summary>
    /// <param name="method">Candidate method.</param>
    /// <returns>Returns <see langword="true"/> if weaveable.</returns>
    public static bool HasWeaveableBody(MethodDefinition method)
    {
        if (method.IsAbstract || method.IsRuntime || method.IsPInvokeImpl)
        {
            return false;
        }

        return method.HasBody;
    }

    /// <summary>Whether the method is an ILWeaving / AsyncILWeaving weave template (should be excluded from match targets).</summary>
    /// <param name="method">Candidate method.</param>
    /// <returns>Returns <see langword="true"/> if it is a weave template.</returns>
    public static bool IsWeaveTemplate(MethodDefinition method)
    {
        if (!method.HasCustomAttributes)
        {
            return false;
        }

        foreach (var attribute in method.CustomAttributes)
        {
            var fullName = attribute.AttributeType.FullName;
            var name = attribute.AttributeType.Name;
            if (SharpWeaverMetadata.IsWeaveAttribute(fullName, name)
                || SharpWeaverMetadata.IsAsyncWeaveAttribute(fullName, name))
            {
                return true;
            }

            if (SharpWeaverMetadata.IsWeaveCallSiteAttribute(fullName, name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the method or declaring type is compiler-generated and should be excluded from wildcard matching.</summary>
    /// <param name="method">Candidate method.</param>
    /// <returns>Returns <see langword="true"/> if compiler-generated.</returns>
    public static bool IsCompilerGenerated(MethodDefinition method)
    {
        var name = method.Name;
        if (name.Contains('<')
            || name.Contains('>')
            || name.StartsWith("__", StringComparison.Ordinal))
        {
            return true;
        }

        var typeName = method.DeclaringType.FullName;
        return typeName.Contains('<')
            || typeName.Contains('>')
            || typeName.Contains("<<", StringComparison.Ordinal);
    }

    /// <summary>Whether the method name is compiler-generated (state machines, lambdas, etc.) and should be excluded from wildcard matching.</summary>
    /// <param name="method">Candidate method.</param>
    /// <returns>Returns <see langword="true"/> if compiler-generated.</returns>
    public static bool IsCompilerGeneratedName(MethodDefinition method) => IsCompilerGenerated(method);

    /// <summary>
    /// Whether the method should not be a wildcard weave target because it is accessed via reflection for metadata (e.g., instantiating <see cref="Attribute"/>).
    /// </summary>
    /// <param name="method">Candidate method.</param>
    /// <returns>Returns <see langword="true"/> if it should be excluded.</returns>
    public static bool IsReflectionUnsafeWildcardTarget(MethodDefinition method) =>
        DerivesFromAttribute(method.DeclaringType);

    /// <summary>Whether the method is eligible as a wildcard/enumeration match candidate for sync weaving.</summary>
    /// <param name="method">Candidate method.</param>
    /// <param name="weaveTemplateCallees">Set of methods directly called by weave templates, to exclude from wildcard matching.</param>
    /// <returns>Returns <see langword="true"/> if eligible.</returns>
    public static bool IsSyncWeaveCandidate(
        MethodDefinition method,
        IReadOnlySet<MethodDefinition>? weaveTemplateCallees = null) =>
        IsWildcardMatchCandidateCore(method, weaveTemplateCallees)
        && !IlTypeHelper.IsByReferenceReturn(method.ReturnType)
        && !AsyncMethodHelper.IsCompilerAsyncMethod(method);

    /// <summary>Whether the method is eligible as a target candidate for a specific sync weave.</summary>
    /// <param name="method">Candidate method.</param>
    /// <param name="weave">Sync weave information.</param>
    /// <param name="weaveTemplateCallees">Set of methods directly called by weave templates, to exclude from wildcard matching.</param>
    /// <returns>Returns <see langword="true"/> if eligible.</returns>
    public static bool IsSyncWeaveCandidate(
        MethodDefinition method,
        WeaveInfo weave,
        IReadOnlySet<MethodDefinition>? weaveTemplateCallees = null) =>
        IsSyncWeaveCandidate(method, weaveTemplateCallees)
        && (!weave.ExcludeAsyncLikeReturn || !AsyncMethodHelper.IsAsyncLikeReturn(method.ReturnType));

    /// <summary>Whether the method is eligible as a wildcard/enumeration match candidate for async weaving.</summary>
    /// <param name="method">Candidate method (user-visible outer async method).</param>
    /// <param name="weaveTemplateCallees">Set of methods directly called by weave templates, to exclude from wildcard matching.</param>
    /// <returns>Returns <see langword="true"/> if eligible.</returns>
    public static bool IsAsyncWeaveCandidate(
        MethodDefinition method,
        IReadOnlySet<MethodDefinition>? weaveTemplateCallees = null)
    {
        if (!IsWildcardMatchCandidateCore(method, weaveTemplateCallees))
        {
            return false;
        }

        if (!AsyncMethodHelper.IsCompilerAsyncMethod(method))
        {
            return false;
        }

        if (!AsyncMethodHelper.IsAsyncLikeReturn(method.ReturnType))
        {
            return false;
        }

        return AsyncMethodHelper.TryResolveMoveNext(method, out _, out _);
    }

    /// <summary>Whether the method is eligible as a wildcard/enumeration match candidate (regardless of sync/async).</summary>
    /// <param name="method">Candidate method.</param>
    /// <param name="weaveTemplateCallees">Set of methods directly called by weave templates, to exclude from wildcard matching.</param>
    /// <returns>Returns <see langword="true"/> if eligible.</returns>
    public static bool IsWildcardMatchCandidate(
        MethodDefinition method,
        IReadOnlySet<MethodDefinition>? weaveTemplateCallees = null) =>
        IsWildcardMatchCandidateCore(method, weaveTemplateCallees);

    private static bool IsWildcardMatchCandidateCore(
        MethodDefinition method,
        IReadOnlySet<MethodDefinition>? weaveTemplateCallees)
    {
        if (!HasWeaveableBody(method))
        {
            return false;
        }

        if (IsWeaveTemplate(method))
        {
            return false;
        }

        if (IsCompilerGeneratedName(method))
        {
            return false;
        }

        if (IsReflectionUnsafeWildcardTarget(method))
        {
            return false;
        }

        if (weaveTemplateCallees != null && weaveTemplateCallees.Contains(method))
        {
            return false;
        }

        return true;
    }

    /// <summary>Whether the method requires handling by a weave template declared as generic-aware.</summary>
    /// <param name="method">Candidate method.</param>
    /// <returns>Returns <see langword="true"/> if the method or declaring type has open generic parameters.</returns>
    public static bool RequiresGenericWeave(MethodDefinition method) =>
        method.HasGenericParameters || IsOpenGenericDeclaringTypeTarget(method);

    private static bool IsOpenGenericDeclaringTypeTarget(MethodDefinition method)
    {
        var current = method.DeclaringType;
        while (current != null)
        {
            if (current.HasGenericParameters)
            {
                return true;
            }

            current = current.DeclaringType;
        }

        return false;
    }

    private static bool DerivesFromAttribute(TypeDefinition type)
    {
        var current = type;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (current != null && visited.Add(current.FullName))
        {
            if (current.FullName == SystemAttributeFullName)
            {
                return true;
            }

            if (current.BaseType == null)
            {
                break;
            }

            if (current.BaseType.FullName == SystemAttributeFullName)
            {
                return true;
            }

            try
            {
                current = current.BaseType.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                break;
            }
        }

        return false;
    }
}
