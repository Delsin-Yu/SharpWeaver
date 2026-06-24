using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Resolves types and methods in reference assemblies and the target assembly.</summary>
public interface IAssemblyResolver
{
    /// <summary>Resolves a type by fully qualified name.</summary>
    /// <param name="fullTypeName">Type fully qualified name.</param>
    /// <param name="type">Resolved type definition.</param>
    /// <param name="error">Human-readable error message on failure.</param>
    /// <returns>Returns <see langword="true"/> on successful resolution.</returns>
    bool TryResolveType(string fullTypeName, out TypeDefinition? type, out string? error);

    /// <summary>Resolves a target method by parsed signature.</summary>
    /// <param name="signature">Structured signature.</param>
    /// <param name="method">Resolved method definition.</param>
    /// <param name="error">Human-readable error message on failure.</param>
    /// <returns>Returns <see langword="true"/> on successful resolution.</returns>
    bool TryResolveMethod(ParsedSignature signature, out MethodDefinition? method, out string? error);
}
