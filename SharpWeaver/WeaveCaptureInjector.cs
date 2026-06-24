using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver;

/// <summary>Weave method parameter metadata capture kinds.</summary>
public enum WeaveCaptureKind
{
    /// <summary>Non-capture parameter.</summary>
    None,

    /// <summary>Injects the target method name.</summary>
    MethodName,

    /// <summary>Injects the target's declaring type fully qualified name.</summary>
    TypeName,

    /// <summary>Injects the PDB first sequence point line number.</summary>
    LineNumber,

    /// <summary>Injects the PDB first sequence point document path.</summary>
    FilePath,

    /// <summary>Injects the open generic parameter type array visible on the target method.</summary>
    TypeParams,
}

/// <summary>Resolves capture slots from weave method parameter attributes and generates constant-loading IL during splicing.</summary>
public static class WeaveCaptureInjector
{
    /// <summary>Resolves the capture type for each parameter of the weave method.</summary>
    /// <param name="weaveMethod">Weave method.</param>
    /// <returns>Array of capture kinds aligned with parameter indices.</returns>
    public static WeaveCaptureKind[] GetCaptureKinds(MethodDefinition weaveMethod)
    {
        var kinds = new WeaveCaptureKind[weaveMethod.Parameters.Count];
        for (var i = 0; i < weaveMethod.Parameters.Count; i++)
        {
            kinds[i] = GetParameterCaptureKind(weaveMethod.Parameters[i]);
        }

        return kinds;
    }

    /// <summary>Whether the weave method uses the wildcard mode parameter contract.</summary>
    /// <param name="weave">Weave information.</param>
    /// <returns>Returns <see langword="true"/> for wildcard mode.</returns>
    public static bool IsWildcardWeave(WeaveInfo weave) => weave.Pattern.IsWildcard;

    /// <summary>
    /// Generates constant-loading instructions for capture parameters, replacing <c>ldarg</c> in the weave prefix.
    /// </summary>
    /// <param name="captureKind">Capture kind.</param>
    /// <param name="targetMethod">Target method being woven.</param>
    /// <param name="module">Target module.</param>
    /// <returns>Constant-loading instruction; returns <see langword="null"/> for non-capture types.</returns>
    public static Instruction? CreateCaptureLoadInstruction(
        WeaveCaptureKind captureKind,
        MethodDefinition targetMethod,
        ModuleDefinition module)
    {
        return captureKind switch
        {
            WeaveCaptureKind.MethodName => Instruction.Create(OpCodes.Ldstr, targetMethod.Name),
            WeaveCaptureKind.TypeName => Instruction.Create(
                OpCodes.Ldstr,
                targetMethod.DeclaringType.FullName),
            WeaveCaptureKind.LineNumber => Instruction.Create(
                OpCodes.Ldc_I4,
                GetFirstSequencePointLine(targetMethod)),
            WeaveCaptureKind.FilePath => Instruction.Create(
                OpCodes.Ldstr,
                GetFirstSequencePointFilePath(targetMethod)),
            _ => null,
        };
    }

    /// <summary>Generates IL to load the <see cref="Type"/> array of open generic parameters visible on the target method.</summary>
    /// <param name="targetMethod">Target method being woven.</param>
    /// <param name="module">Target module.</param>
    /// <returns>Instruction sequence that leaves a <see cref="Type"/> array on the stack.</returns>
    public static Instruction[] CreateTypeParamsArrayLoadInstructions(
        MethodDefinition targetMethod,
        ModuleDefinition module)
    {
        var genericParameters = GetVisibleGenericParameters(targetMethod).Cast<TypeReference>().ToList();
        return CreateTypeParamsArrayLoadInstructions(genericParameters, module);
    }

    /// <summary>Generates IL to load the <see cref="Type"/> array of open generic parameters within an async state machine <c>MoveNext</c>.</summary>
    /// <param name="outerMethod">User-visible outer async method.</param>
    /// <param name="targetStateMachine">Target async state machine type.</param>
    /// <param name="module">Target module.</param>
    /// <returns>Instruction sequence that leaves a <see cref="Type"/> array on the stack.</returns>
    public static Instruction[] CreateAsyncTypeParamsArrayLoadInstructions(
        MethodDefinition outerMethod,
        TypeDefinition targetStateMachine,
        ModuleDefinition module)
    {
        var genericParameters = GetVisibleDeclaringTypeGenericParameters(outerMethod.DeclaringType)
            .Cast<TypeReference>()
            .ToList();
        for (var i = 0; i < outerMethod.GenericParameters.Count; i++)
        {
            genericParameters.Add(i < targetStateMachine.GenericParameters.Count
                ? targetStateMachine.GenericParameters[i]
                : outerMethod.GenericParameters[i]);
        }

        return CreateTypeParamsArrayLoadInstructions(genericParameters, module);
    }

    private static Instruction[] CreateTypeParamsArrayLoadInstructions(
        IReadOnlyList<TypeReference> genericParameters,
        ModuleDefinition module)
    {
        var instructions = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldc_I4, genericParameters.Count),
            Instruction.Create(OpCodes.Newarr, module.ImportReference(typeof(Type))),
        };

        if (genericParameters.Count == 0)
        {
            return instructions.ToArray();
        }

        var getTypeFromHandle = module.ImportReference(
            typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), [typeof(RuntimeTypeHandle)])!);
        for (var i = 0; i < genericParameters.Count; i++)
        {
            instructions.Add(Instruction.Create(OpCodes.Dup));
            instructions.Add(Instruction.Create(OpCodes.Ldc_I4, i));
            instructions.Add(Instruction.Create(OpCodes.Ldtoken, module.ImportReference(genericParameters[i])));
            instructions.Add(Instruction.Create(OpCodes.Call, getTypeFromHandle));
            instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }

        return instructions.ToArray();
    }

    /// <summary>Whether the first parameter of the weave method is an <c>object?</c> instance slot (wildcard mode).</summary>
    /// <param name="weaveMethod">Weave method.</param>
    /// <returns>Returns <see langword="true"/> if an object instance slot exists.</returns>
    public static bool HasObjectInstanceSlot(MethodDefinition weaveMethod)
    {
        if (weaveMethod.Parameters.Count == 0)
        {
            return false;
        }

        var first = weaveMethod.Parameters[0].ParameterType;
        return first.FullName == "System.Object" && !first.IsByReference;
    }

    private static WeaveCaptureKind GetParameterCaptureKind(ParameterDefinition parameter)
    {
        if (!parameter.HasCustomAttributes)
        {
            return WeaveCaptureKind.None;
        }

        foreach (var attribute in parameter.CustomAttributes)
        {
            var fullName = attribute.AttributeType.FullName;
            var name = attribute.AttributeType.Name;

            if (fullName == SharpWeaverMetadata.WeaveMethodNameAttribute || name == nameof(WeaveMethodNameAttribute))
            {
                return WeaveCaptureKind.MethodName;
            }

            if (fullName == SharpWeaverMetadata.WeaveTypeNameAttribute || name == nameof(WeaveTypeNameAttribute))
            {
                return WeaveCaptureKind.TypeName;
            }

            if (fullName == SharpWeaverMetadata.WeaveLineNumberAttribute || name == nameof(WeaveLineNumberAttribute))
            {
                return WeaveCaptureKind.LineNumber;
            }

            if (fullName == SharpWeaverMetadata.WeaveFilePathAttribute || name == nameof(WeaveFilePathAttribute))
            {
                return WeaveCaptureKind.FilePath;
            }

            if (fullName == SharpWeaverMetadata.WeaveTypeParamsAttribute || name == nameof(WeaveTypeParamsAttribute))
            {
                return WeaveCaptureKind.TypeParams;
            }
        }

        return WeaveCaptureKind.None;
    }

    private static List<GenericParameter> GetVisibleGenericParameters(MethodDefinition method)
    {
        var result = GetVisibleDeclaringTypeGenericParameters(method.DeclaringType);
        result.AddRange(method.GenericParameters);
        return result;
    }

    private static List<GenericParameter> GetVisibleDeclaringTypeGenericParameters(TypeDefinition declaringType)
    {
        var result = new List<GenericParameter>();
        var declaringTypes = new Stack<TypeDefinition>();
        while (declaringType != null)
        {
            declaringTypes.Push(declaringType);
            declaringType = declaringType.DeclaringType;
        }

        while (declaringTypes.Count > 0)
        {
            result.AddRange(declaringTypes.Pop().GenericParameters);
        }

        return result;
    }

    private static int GetFirstSequencePointLine(MethodDefinition method)
    {
        if (!method.HasBody || method.DebugInformation == null)
        {
            return 0;
        }

        foreach (var sequencePoint in method.DebugInformation.SequencePoints)
        {
            if (sequencePoint.IsHidden)
            {
                continue;
            }

            if (sequencePoint.StartLine > 0)
            {
                return sequencePoint.StartLine;
            }
        }

        return 0;
    }

    private static string GetFirstSequencePointFilePath(MethodDefinition method)
    {
        if (!method.HasBody || method.DebugInformation == null)
        {
            return string.Empty;
        }

        foreach (var sequencePoint in method.DebugInformation.SequencePoints)
        {
            if (sequencePoint.IsHidden)
            {
                continue;
            }

            if (sequencePoint.Document?.Url is { Length: > 0 } url)
            {
                return url;
            }
        }

        return string.Empty;
    }
}
