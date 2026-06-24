using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Enumerates override candidates that override a specified base/virtual method in the target assembly.</summary>
public static class OverrideMatcher
{
    /// <summary>
    /// Enumerates all virtual method overrides of <paramref name="targetMethod"/> in the target assembly.
    /// </summary>
    /// <param name="wovenModule">Target module.</param>
    /// <param name="targetMethod">Resolved target base/virtual method.</param>
    /// <returns>List of matching override methods.</returns>
    public static IReadOnlyList<MethodDefinition> FindOverrides(ModuleDefinition wovenModule, MethodDefinition targetMethod)
    {
        var matches = new List<MethodDefinition>();
        var targetType = targetMethod.DeclaringType;

        foreach (var type in wovenModule.EnumerateAllTypes())
        {
            if (!DerivesFrom(type, targetType))
            {
                continue;
            }

            foreach (var method in type.Methods)
            {
                if (!method.IsVirtual || method.IsAbstract || method.IsConstructor)
                {
                    continue;
                }

                if (method.Module != wovenModule)
                {
                    continue;
                }

                if (IsTargetMethodDefinition(method, targetMethod))
                {
                    continue;
                }

                if (!MethodsMatch(method, targetMethod))
                {
                    continue;
                }

                matches.Add(method);
            }
        }

        return matches
            .OrderBy(method => method.DeclaringType.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsTargetMethodDefinition(MethodDefinition method, MethodDefinition targetMethod)
    {
        return method.DeclaringType.FullName == targetMethod.DeclaringType.FullName
            && MethodsMatch(method, targetMethod);
    }

    private static bool DerivesFrom(TypeDefinition type, TypeDefinition targetType)
    {
        var current = type;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (current != null && visited.Add(current.FullName))
        {
            if (current.FullName == targetType.FullName)
            {
                return true;
            }

            if (current.BaseType == null)
            {
                break;
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

    private static bool MethodsMatch(MethodDefinition left, MethodDefinition right)
    {
        if (left.Name != right.Name)
        {
            return false;
        }

        if (left.Parameters.Count != right.Parameters.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Count; i++)
        {
            if (left.Parameters[i].ParameterType.FullName != right.Parameters[i].ParameterType.FullName)
            {
                return false;
            }
        }

        return left.ReturnType.FullName == right.ReturnType.FullName;
    }
}
