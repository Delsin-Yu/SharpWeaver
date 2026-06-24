using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver;

/// <summary>Helper methods for identifying compiler-generated async methods and state machine <c>MoveNext</c> methods.</summary>
public static class AsyncMethodHelper
{
    private const string AsyncStateMachineAttributeFullName =
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute";

    private static readonly HashSet<string> AsyncLikeReturnTypeNames = new(StringComparer.Ordinal)
    {
        "System.Threading.Tasks.Task",
        "GodotTask.GDTask",
        "System.Threading.Tasks.ValueTask",
    };

    private static readonly HashSet<string> GenericAsyncLikeReturnTypeNames = new(StringComparer.Ordinal)
    {
        "System.Threading.Tasks.Task`1",
        "GodotTask.GDTask`1",
        "System.Threading.Tasks.ValueTask`1",
    };

    /// <summary>Whether the method has the <c>[AsyncStateMachine]</c> attribute (compiler-generated async method).</summary>
    /// <param name="method">Candidate method.</param>
    /// <returns>Returns <see langword="true"/> if it is a compiler-generated async method.</returns>
    public static bool IsCompilerAsyncMethod(MethodDefinition method)
    {
        if (!method.HasCustomAttributes)
        {
            return false;
        }

        foreach (var attribute in method.CustomAttributes)
        {
            if (attribute.AttributeType.FullName == AsyncStateMachineAttributeFullName
                || attribute.AttributeType.Name == "AsyncStateMachineAttribute")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the return type is async-like (<c>Task</c>, <c>Task&lt;T&gt;</c>, <c>GDTask</c>, <c>GDTask&lt;T&gt;</c>, <c>ValueTask</c>, etc.).</summary>
    /// <param name="returnType">Method return type.</param>
    /// <returns>Returns <see langword="true"/> if async-like.</returns>
    public static bool IsAsyncLikeReturn(TypeReference returnType)
    {
        if (returnType is GenericInstanceType generic)
        {
            return GenericAsyncLikeReturnTypeNames.Contains(generic.ElementType.FullName);
        }

        var type = returnType;

        while (type is TypeSpecification specification)
        {
            type = specification.ElementType;
        }

        if (type.MetadataType == MetadataType.Void)
        {
            return false;
        }

        if (AsyncLikeReturnTypeNames.Contains(type.FullName))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves the state machine type and its <c>MoveNext</c> implementation from a compiler-generated async method.
    /// </summary>
    /// <param name="outerMethod">User-visible async method.</param>
    /// <param name="moveNext">Output: <c>MoveNext</c> method definition.</param>
    /// <param name="stateMachineType">Output: State machine nested type.</param>
    /// <returns>Returns <see langword="true"/> on success.</returns>
    public static bool TryResolveMoveNext(
        MethodDefinition outerMethod,
        out MethodDefinition moveNext,
        out TypeDefinition stateMachineType)
    {
        moveNext = null!;
        stateMachineType = null!;

        if (!IsCompilerAsyncMethod(outerMethod))
        {
            return false;
        }

        var stateMachineTypeRef = GetStateMachineTypeReference(outerMethod);
        if (stateMachineTypeRef == null)
        {
            return false;
        }

        try
        {
            stateMachineType = stateMachineTypeRef.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return false;
        }

        foreach (var method in stateMachineType.Methods)
        {
            if (method.Name == "MoveNext" && method.HasBody)
            {
                if (!TryStripAsyncMethodBodyDebugInfo(method))
                {
                    return false;
                }

                moveNext = method;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes custom debug information from the async state machine PDB to prevent Cecil from failing on dangling instruction references when reading <c>MoveNext</c> IL.
    /// </summary>
    /// <param name="method">State machine <c>MoveNext</c> or async outer method.</param>
    public static void StripAsyncMethodBodyDebugInfo(MethodDefinition method)
    {
        _ = TryStripAsyncMethodBodyDebugInfo(method);
    }

    private static bool TryStripAsyncMethodBodyDebugInfo(MethodDefinition method)
    {
        MethodDebugInformation debugInfo;
        try
        {
            debugInfo = method.DebugInformation;
        }
        catch (ArgumentNullException ex) when (ex.ParamName == "instruction")
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        if (debugInfo is null)
        {
            return true;
        }

        for (var i = debugInfo.CustomDebugInformations.Count - 1; i >= 0; i--)
        {
            var kind = debugInfo.CustomDebugInformations[i].Kind;
            if (kind is CustomDebugInformationKind.AsyncMethodBody
                or CustomDebugInformationKind.StateMachineScope)
            {
                debugInfo.CustomDebugInformations.RemoveAt(i);
            }
        }

        return true;
    }

    /// <summary>Reads the state machine type reference from the async method's custom attribute.</summary>
    /// <param name="outerMethod">User-visible async method.</param>
    /// <returns>State machine type reference; returns <see langword="null"/> when missing.</returns>
    public static TypeReference? GetStateMachineTypeReference(MethodDefinition outerMethod)
    {
        foreach (var attribute in outerMethod.CustomAttributes)
        {
            if (attribute.AttributeType.FullName != AsyncStateMachineAttributeFullName
                && attribute.AttributeType.Name != "AsyncStateMachineAttribute")
            {
                continue;
            }

            if (attribute.ConstructorArguments.Count > 0
                && attribute.ConstructorArguments[0].Value is TypeReference typeReference)
            {
                return typeReference;
            }
        }

        return null;
    }
}
