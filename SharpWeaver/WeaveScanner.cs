using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Scans the target assembly to discover weave methods with <see cref="WeaveAttribute"/> / <see cref="AsyncWeaveAttribute"/>.</summary>
public static class WeaveScanner
{
    private const string TargetSignaturePropertyName = "TargetSignature";
    private const string ExcludedSignaturePropertyName = "ExcludedSignature";
    private const string PriorityPropertyName = "Priority";
    private const string GenericWeavePropertyName = "GenericWeave";

    /// <summary>Scans the assembly and returns the list of weave methods and error messages.</summary>
    /// <param name="wovenAssembly">Target assembly to be woven.</param>
    /// <param name="weaves">List of discovered weave methods.</param>
    /// <param name="errors">Error messages encountered during scanning.</param>
    public static void Scan(
        AssemblyDefinition wovenAssembly,
        out IReadOnlyList<WeaveInfo> weaves,
        out IReadOnlyList<string> errors)
    {
        var foundWeaves = new List<WeaveInfo>();
        var foundErrors = new List<string>();
        var discoveryOrder = 0;

        foreach (var type in wovenAssembly.MainModule.EnumerateAllTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasCustomAttributes)
                {
                    continue;
                }

                var hasSync = false;
                var hasAsync = false;

                foreach (var attribute in method.CustomAttributes)
                {
                    var attrName = attribute.AttributeType.FullName;
                    var shortName = attribute.AttributeType.Name;
                    if (SharpWeaverMetadata.IsWeaveAttribute(attrName, shortName))
                    {
                        hasSync = true;
                    }
                    else if (SharpWeaverMetadata.IsAsyncWeaveAttribute(attrName, shortName))
                    {
                        hasAsync = true;
                    }
                }

                if (hasSync && hasAsync)
                {
                    foundErrors.Add(
                        $"编织方法 '{type.FullName}.{method.Name}' 不得同时标注 [Weave] 与 [AsyncWeave]。");
                    continue;
                }

                if (!hasSync && !hasAsync)
                {
                    continue;
                }

                var excludePatterns = ReadExcludePatterns(method, type.FullName, foundErrors);
                var excludeAsyncLikeReturn = HasExcludeAsyncLikeReturn(method);

                if (!method.IsStatic)
                {
                    foundErrors.Add(
                        $"Weave 编织方法 '{type.FullName}.{method.Name}' 必须为 static。");
                    continue;
                }

                foreach (var attribute in method.CustomAttributes)
                {
                    var attrName = attribute.AttributeType.FullName;
                    var shortName = attribute.AttributeType.Name;
                    var isSyncAttr = SharpWeaverMetadata.IsWeaveAttribute(attrName, shortName);
                    var isAsyncAttr = SharpWeaverMetadata.IsAsyncWeaveAttribute(attrName, shortName);

                    if (!isSyncAttr && !isAsyncAttr)
                    {
                        continue;
                    }

                    var sig = ReadTargetSignature(attribute);
                    if (sig == null)
                    {
                        var attrLabel = isAsyncAttr ? "[AsyncWeave]" : "[Weave]";
                        foundErrors.Add(
                            $"编织方法 '{type.FullName}.{method.Name}' 的 {attrLabel} 特性缺少 TargetSignature 参数。");
                        continue;
                    }

                    if (!TryReadPriority(attribute, out var priority, out var priorityError))
                    {
                        foundErrors.Add(
                            $"编织方法 '{type.FullName}.{method.Name}'：{priorityError}");
                        continue;
                    }

                    var genericWeave = ReadGenericWeave(attribute);

                    if (!SignaturePatternParser.TryParse(sig, out var pattern, out var patternError))
                    {
                        foundErrors.Add(
                            $"编织方法 '{type.FullName}.{method.Name}'：{patternError}");
                        continue;
                    }

                    foundWeaves.Add(new WeaveInfo(
                        sig,
                        pattern!,
                        priority,
                        method,
                        type.FullName,
                        discoveryOrder++,
                        excludePatterns,
                        isAsyncAttr,
                        genericWeave,
                        excludeAsyncLikeReturn));
                }
            }
        }

        weaves = foundWeaves;
        errors = foundErrors;
    }

    private static string? ReadTargetSignature(CustomAttribute attribute)
    {
        if (attribute.ConstructorArguments.Count > 0
            && attribute.ConstructorArguments[0].Value is string ctorValue
            && !string.IsNullOrWhiteSpace(ctorValue))
        {
            return ctorValue;
        }

        foreach (var property in attribute.Properties)
        {
            if (property.Name == TargetSignaturePropertyName
                && property.Argument.Value is string propertyValue
                && !string.IsNullOrWhiteSpace(propertyValue))
            {
                return propertyValue;
            }
        }

        return null;
    }

    private static IReadOnlyList<SignaturePattern> ReadExcludePatterns(
        MethodDefinition method,
        string declaringTypeFullName,
        List<string> errors)
    {
        var excludePatterns = new List<SignaturePattern>();
        foreach (var attribute in method.CustomAttributes)
        {
            var attrName = attribute.AttributeType.FullName;
            var shortName = attribute.AttributeType.Name;
            var isExcludeAttr = attrName == SharpWeaverMetadata.WeaveExcludeAttribute
                || shortName == nameof(WeaveExcludeAttribute);
            if (!isExcludeAttr)
            {
                continue;
            }

            var excludedSignature = ReadExcludedSignature(attribute);
            if (excludedSignature == null)
            {
                errors.Add(
                    $"编织方法 '{declaringTypeFullName}.{method.Name}' 的 [WeaveExclude] 特性缺少 ExcludedSignature 参数。");
                continue;
            }

            if (!SignaturePatternParser.TryParse(excludedSignature, out var pattern, out var patternError))
            {
                errors.Add(
                    $"编织方法 '{declaringTypeFullName}.{method.Name}' 的 [WeaveExclude]：{patternError}");
                continue;
            }

            excludePatterns.Add(pattern!);
        }

        return excludePatterns;
    }

    private static bool HasExcludeAsyncLikeReturn(MethodDefinition method)
    {
        foreach (var attribute in method.CustomAttributes)
        {
            var attrName = attribute.AttributeType.FullName;
            var shortName = attribute.AttributeType.Name;
            if (attrName == SharpWeaverMetadata.WeaveExcludeAsyncLikeReturnAttribute
                || shortName == nameof(WeaveExcludeAsyncLikeReturnAttribute))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ReadExcludedSignature(CustomAttribute attribute)
    {
        if (attribute.ConstructorArguments.Count > 0
            && attribute.ConstructorArguments[0].Value is string ctorValue
            && !string.IsNullOrWhiteSpace(ctorValue))
        {
            return ctorValue;
        }

        foreach (var property in attribute.Properties)
        {
            if (property.Name == ExcludedSignaturePropertyName
                && property.Argument.Value is string propertyValue
                && !string.IsNullOrWhiteSpace(propertyValue))
            {
                return propertyValue;
            }
        }

        return null;
    }

    private static bool TryReadPriority(CustomAttribute attribute, out int priority, out string? error)
    {
        priority = 0;
        error = null;

        if (attribute.ConstructorArguments.Count > 1
            && attribute.ConstructorArguments[1].Value is int ctorPriority)
        {
            priority = ctorPriority;
            return true;
        }

        foreach (var property in attribute.Properties)
        {
            if (property.Name == PriorityPropertyName
                && property.Argument.Value is int propertyPriority)
            {
                priority = propertyPriority;
                return true;
            }
        }

        error = "编织特性缺少必需的 priority 参数。";
        return false;
    }

    private static bool ReadGenericWeave(CustomAttribute attribute)
    {
        if (attribute.ConstructorArguments.Count > 2
            && attribute.ConstructorArguments[2].Value is bool ctorValue)
        {
            return ctorValue;
        }

        foreach (var property in attribute.Properties)
        {
            if (property.Name == GenericWeavePropertyName
                && property.Argument.Value is bool propertyValue)
            {
                return propertyValue;
            }
        }

        return false;
    }
}
