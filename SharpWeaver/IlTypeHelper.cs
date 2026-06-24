using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Mono.Cecil type determination helper methods.</summary>
public static class IlTypeHelper
{
    /// <summary>
    /// Determines whether the method return type is void in IL semantics (including <c>void modreq(...)</c> and other modified forms).
    /// </summary>
    /// <param name="returnType">Method return type reference.</param>
    /// <returns>Whether it is <see cref="MetadataType.Void"/> after stripping type modifiers.</returns>
    public static bool IsVoidReturn(TypeReference returnType)
    {
        var type = returnType;
        while (type is TypeSpecification specification)
        {
            type = specification.ElementType;
        }

        return type.MetadataType == MetadataType.Void;
    }
}
