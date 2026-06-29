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

    /// <summary>
    /// Determines whether the method return type is a managed reference, including modified forms such as
    /// <c>T&amp; modreq(InAttribute)</c> emitted for <c>ref readonly</c> returns.
    /// </summary>
    /// <param name="returnType">Method return type reference.</param>
    /// <returns>Whether the return type is a managed reference after stripping return modifiers.</returns>
    public static bool IsByReferenceReturn(TypeReference returnType)
    {
        var type = returnType;
        while (type is RequiredModifierType or OptionalModifierType)
        {
            type = ((TypeSpecification)type).ElementType;
        }

        return type.IsByReference;
    }
}
