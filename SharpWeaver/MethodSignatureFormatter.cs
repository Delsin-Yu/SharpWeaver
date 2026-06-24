using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Formats a Cecil method for CLI dry-run output.</summary>
public static class MethodSignatureFormatter
{
    /// <summary>Formats to a <c>TypeName.MethodName(params)</c> display string.</summary>
    /// <param name="method">Method definition.</param>
    /// <returns>Readable signature string.</returns>
    public static string Format(MethodDefinition method)
    {
        var paramList = string.Join(
            ", ",
            method.Parameters.Select(parameter => SignatureParser.FormatTypeNameForDisplay(parameter.ParameterType.FullName)));
        return $"{method.DeclaringType.FullName}.{method.Name}({paramList})";
    }
}
