using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver;

/// <summary>
/// Validates the compatibility of <see cref="WeaveAttribute"/> / <see cref="AsyncWeaveAttribute"/> weave method signatures with their targets.
/// </summary>
public static class WeaveSignatureValidator
{
    private const string TaskFullName = "System.Threading.Tasks.Task";

    /// <summary>
    /// Validates the weave method signature.
    /// </summary>
    /// <param name="weave">Weave method information.</param>
    /// <param name="targetMethod">Resolved target method (base method for exact mode, the method itself for wildcard mode).</param>
    /// <param name="spliceMethod">The method that actually receives the spliced IL (<c>MoveNext</c> for async).</param>
    /// <param name="outerMethod">User-visible outer method used for metadata capture and async validation.</param>
    /// <param name="error">Error message on failure.</param>
    /// <returns>Whether the signature is valid.</returns>
    public static bool TryValidate(
        WeaveInfo weave,
        MethodDefinition targetMethod,
        MethodDefinition spliceMethod,
        MethodDefinition outerMethod,
        out string? error)
    {
        if (weave.IsAsync)
        {
            return TryValidateAsyncWeave(weave, targetMethod, outerMethod, out error);
        }

        if (WeaveCaptureInjector.IsWildcardWeave(weave))
        {
            return TryValidateWildcardWeave(weave, outerMethod, out error);
        }

        return TryValidateExactWeave(weave, targetMethod, spliceMethod, out error);
    }

    private static bool TryValidateExactWeave(
        WeaveInfo weave,
        MethodDefinition targetMethod,
        MethodDefinition overrideMethod,
        out string? error)
    {
        error = null;
        var weaveMethod = weave.WeaveMethod;

        if (!TryValidateSyncCommon(weaveMethod, out error))
        {
            return false;
        }

        var instanceOffset = targetMethod.HasThis ? 1 : 0;
        var isVoidTarget = IlTypeHelper.IsVoidReturn(targetMethod.ReturnType);
        var minParamCount = targetMethod.Parameters.Count + instanceOffset;
        var maxParamCount = minParamCount + (isVoidTarget ? 0 : 1);

        if (weaveMethod.Parameters.Count < minParamCount || weaveMethod.Parameters.Count > maxParamCount)
        {
            error =
                $"ILWeaving 编织方法 '{weave.WeaveMethodDisplayName}' 的参数数量（{weaveMethod.Parameters.Count}）" +
                $"不符合预期（{minParamCount}~{maxParamCount}）。" +
                $"期望：{(instanceOffset > 0 ? "实例参数 + " : string.Empty)}" +
                $"{targetMethod.Parameters.Count} 个目标参数" +
                $"{(isVoidTarget ? string.Empty : " + 可选的 ref TReturn returnValue")}。";
            return false;
        }

        if (targetMethod.HasThis && weaveMethod.Parameters.Count > 0)
        {
            var instanceParam = weaveMethod.Parameters[0];
            if (instanceParam.ParameterType.IsByReference)
            {
                error =
                    $"ILWeaving 编织方法 '{weave.WeaveMethodDisplayName}' 的实例参数 '{instanceParam.Name}' " +
                    $"必须按值传递（by value），不得为 ref。";
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateWildcardWeave(
        WeaveInfo weave,
        MethodDefinition outerMethod,
        out string? error)
    {
        error = null;
        var weaveMethod = weave.WeaveMethod;

        if (!TryValidateSyncCommon(weaveMethod, out error))
        {
            return false;
        }

        return TryValidateWildcardCaptureContract(weave, weaveMethod, out error);
    }

    private static bool TryValidateAsyncWeave(
        WeaveInfo weave,
        MethodDefinition targetMethod,
        MethodDefinition outerMethod,
        out string? error)
    {
        error = null;
        var weaveMethod = weave.WeaveMethod;

        if (!TryValidateAsyncCommon(weaveMethod, out error))
        {
            return false;
        }

        if (!WeaveMethodFilter.IsAsyncWeaveCandidate(outerMethod))
        {
            error =
                $"AsyncILWeaving 编织 '{weave.WeaveMethodDisplayName}' 的目标 " +
                $"'{MethodSignatureFormatter.Format(outerMethod)}' 不是可编织的编译器 async 方法。";
            return false;
        }

        if (WeaveCaptureInjector.IsWildcardWeave(weave))
        {
            return TryValidateWildcardCaptureContract(weave, weaveMethod, out error);
        }

        _ = targetMethod;
        return true;
    }

    private static bool TryValidateWildcardCaptureContract(
        WeaveInfo weave,
        MethodDefinition weaveMethod,
        out string? error)
    {
        error = null;
        var captureKinds = WeaveCaptureInjector.GetCaptureKinds(weaveMethod);
        var hasMethodName = false;
        var hasTypeName = false;
        var hasLineNumber = false;
        var hasFilePath = false;
        var hasTypeParams = false;
        var paramIndex = 0;

        if (WeaveCaptureInjector.HasObjectInstanceSlot(weaveMethod))
        {
            var instanceParam = weaveMethod.Parameters[0];
            if (instanceParam.ParameterType.IsByReference)
            {
                error =
                    $"编织方法 '{weave.WeaveMethodDisplayName}' 的实例参数必须按值传递（object?）。";
                return false;
            }

            paramIndex = 1;
        }

        for (; paramIndex < weaveMethod.Parameters.Count; paramIndex++)
        {
            var param = weaveMethod.Parameters[paramIndex];
            if (param.ParameterType.IsByReference)
            {
                error =
                    $"编织方法 '{weave.WeaveMethodDisplayName}' 不得包含 ref 目标参数槽。";
                return false;
            }

            switch (captureKinds[paramIndex])
            {
                case WeaveCaptureKind.MethodName:
                    if (param.ParameterType.FullName != "System.String")
                    {
                        error =
                            $"编织方法 '{weave.WeaveMethodDisplayName}' 的方法名捕获参数必须为 string。";
                        return false;
                    }

                    if (hasMethodName)
                    {
                        error =
                            $"编织方法 '{weave.WeaveMethodDisplayName}' 的方法名捕获参数只能出现一次。";
                        return false;
                    }

                    hasMethodName = true;
                    break;

                case WeaveCaptureKind.TypeName:
                    if (param.ParameterType.FullName != "System.String")
                    {
                        error =
                            $"编织方法 '{weave.WeaveMethodDisplayName}' 的类型名捕获参数必须为 string。";
                        return false;
                    }

                    if (hasTypeName)
                    {
                        error =
                            $"编织方法 '{weave.WeaveMethodDisplayName}' 的类型名捕获参数只能出现一次。";
                        return false;
                    }

                    hasTypeName = true;
                    break;

                case WeaveCaptureKind.LineNumber:
                    if (param.ParameterType.FullName != "System.Int32")
                    {
                        error =
                            $"编织方法 '{weave.WeaveMethodDisplayName}' 的行号捕获参数必须为 int。";
                        return false;
                    }

                    if (hasLineNumber)
                    {
                        error =
                            $"编织方法 '{weave.WeaveMethodDisplayName}' 的行号捕获参数只能出现一次。";
                        return false;
                    }

                    hasLineNumber = true;
                    break;

                case WeaveCaptureKind.FilePath:
                    if (param.ParameterType.FullName != "System.String")
                    {
                        error =
                            $"编织方法 '{weave.WeaveMethodDisplayName}' 的文件路径捕获参数必须为 string。";
                        return false;
                    }

                    if (hasFilePath)
                    {
                        error =
                            $"编织方法 '{weave.WeaveMethodDisplayName}' 的文件路径捕获参数只能出现一次。";
                        return false;
                    }

                    hasFilePath = true;
                    break;

                case WeaveCaptureKind.TypeParams:
                    if (param.ParameterType is not ArrayType arrayType
                        || arrayType.ElementType.FullName != "System.Type")
                    {
                        error =
                            $"编织方法 '{weave.WeaveMethodDisplayName}' 的泛型参数捕获参数必须为 Type[]。";
                        return false;
                    }

                    if (hasTypeParams)
                    {
                        error =
                            $"编织方法 '{weave.WeaveMethodDisplayName}' 的泛型参数捕获参数只能出现一次。";
                        return false;
                    }

                    hasTypeParams = true;
                    break;

                case WeaveCaptureKind.None:
                    error =
                        $"编织方法 '{weave.WeaveMethodDisplayName}' 的参数 '{param.Name}' " +
                        $"不是允许的捕获槽位（通配符模式不允许 per-target 参数）。";
                    return false;
            }
        }

        return true;
    }

    private static bool TryValidateSyncCommon(MethodDefinition weaveMethod, out string? error)
    {
        error = null;

        if (!weaveMethod.IsStatic)
        {
            error = $"ILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' 必须为 static。";
            return false;
        }

        if (weaveMethod.ReturnType.MetadataType != MetadataType.Void)
        {
            error =
                $"ILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' 的返回类型必须为 void。";
            return false;
        }

        if (!weaveMethod.HasBody)
        {
            error =
                $"ILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' " +
                $"必须有方法体（不可为 abstract 或 extern）。";
            return false;
        }

        var markerCount = CountSyncMarkerCalls(weaveMethod);
        if (markerCount == 0)
        {
            error =
                $"ILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' 中缺少 " +
                $"WeaveTemplate.OriginalBody() 标记调用。";
            return false;
        }

        if (markerCount > 1)
        {
            error =
                $"ILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' 中有 {markerCount} 个 " +
                $"WeaveTemplate.OriginalBody() 标记调用，要求恰好一个。";
            return false;
        }

        return true;
    }

    private static bool TryValidateAsyncCommon(MethodDefinition weaveMethod, out string? error)
    {
        error = null;

        if (!weaveMethod.IsStatic)
        {
            error = $"AsyncILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' 必须为 static。";
            return false;
        }

        if (!AsyncMethodHelper.IsCompilerAsyncMethod(weaveMethod))
        {
            error =
                $"AsyncILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' 必须为 async 方法。";
            return false;
        }

        if (!IsTaskReturnType(weaveMethod.ReturnType))
        {
            error =
                $"AsyncILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' " +
                $"的返回类型必须为 System.Threading.Tasks.Task 或 Task<T>。";
            return false;
        }

        if (!AsyncMethodHelper.TryResolveMoveNext(weaveMethod, out var moveNext, out _))
        {
            error =
                $"AsyncILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' 无法解析 MoveNext。";
            return false;
        }

        var markerCount = AsyncAwaitMarkerLocator.CountMarkerCalls(moveNext);
        if (markerCount == 0)
        {
            error =
                $"AsyncILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' 的 MoveNext 中缺少 " +
                $"await WeaveTemplate.OriginalBodyAsync() 标记。";
            return false;
        }

        if (markerCount > 1)
        {
            error =
                $"AsyncILWeaving 编织方法 '{weaveMethod.DeclaringType.FullName}.{weaveMethod.Name}' 的 MoveNext 中有 {markerCount} 个 " +
                $"OriginalBodyAsync 标记，要求恰好一个。";
            return false;
        }

        return true;
    }

    private static bool IsTaskReturnType(TypeReference returnType)
    {
        var type = returnType;
        if (type is GenericInstanceType generic)
        {
            type = generic.ElementType;
        }

        return type.FullName == TaskFullName;
    }

    private static int CountSyncMarkerCalls(MethodDefinition method)
    {
        var count = 0;
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode == OpCodes.Call
                && instruction.Operand is MethodReference mr
                && mr.Name == SharpWeaverMetadata.OriginalBodyMethod
                && mr.DeclaringType.FullName == SharpWeaverMetadata.WeaveTemplate)
            {
                count++;
            }
        }

        return count;
    }
}
