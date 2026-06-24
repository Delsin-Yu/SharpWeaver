using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Mono.Cecil module type enumeration helper methods.</summary>
public static class CecilModuleExtensions
{
    /// <summary>Recursively enumerates all types in the module (including nested types).</summary>
    /// <param name="module">Target module.</param>
    /// <returns>Sequence of type definitions.</returns>
    public static IEnumerable<TypeDefinition> EnumerateAllTypes(this ModuleDefinition module)
    {
        foreach (var type in module.Types)
        {
            foreach (var nested in EnumerateTypeAndNested(type))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<TypeDefinition> EnumerateTypeAndNested(TypeDefinition type)
    {
        yield return type;

        if (!type.HasNestedTypes)
        {
            yield break;
        }

        foreach (var nested in type.NestedTypes)
        {
            foreach (var descendant in EnumerateTypeAndNested(nested))
            {
                yield return descendant;
            }
        }
    }
}
