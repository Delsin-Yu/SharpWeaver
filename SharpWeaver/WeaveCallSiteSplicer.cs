using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace SharpWeaver;

/// <summary>Splices <see cref="WeaveCallSiteAttribute"/> templates around matched call instructions.</summary>
public static class WeaveCallSiteSplicer
{
    /// <summary>Rewrites matched call instructions inside a caller method.</summary>
    /// <param name="callerMethod">Caller method to rewrite.</param>
    /// <param name="callSites">Call sites to replace.</param>
    /// <param name="error">Error message on failure.</param>
    /// <returns>Whether splicing succeeded.</returns>
    public static bool TrySplice(
        MethodDefinition callerMethod,
        IReadOnlyList<CallSiteWeaveMatch> callSites,
        out string? error)
    {
        error = null;
        if (callSites.Count == 0)
        {
            return true;
        }

        if (!callerMethod.HasBody)
        {
            error = $"方法 '{MethodSignatureFormatter.Format(callerMethod)}' 无方法体，无法应用 WeaveCallSite。";
            return false;
        }

        var body = callerMethod.Body;
        var originalInstructions = body.Instructions.ToList();
        var originalHandlers = body.ExceptionHandlers.ToList();
        var callSiteByInstruction = callSites.ToDictionary(callSite => callSite.CallInstruction);
        var instructionMap = new Dictionary<Instruction, Instruction>(originalInstructions.Count);
        var replacementHandlers = new List<ExceptionHandler>();
        var rebuiltInstructions = new List<Instruction>(originalInstructions.Count);

        foreach (var instruction in originalInstructions)
        {
            if (callSiteByInstruction.TryGetValue(instruction, out var callSite))
            {
                if (!BuildCallSiteReplacement(
                        callerMethod,
                        callSite,
                        out var replacement,
                        out var handlers,
                        out var buildError))
                {
                    error = buildError;
                    return false;
                }

                instructionMap[instruction] = replacement[0];
                rebuiltInstructions.AddRange(replacement);
                replacementHandlers.AddRange(handlers);
                continue;
            }

            var clone = CloneInstruction(instruction, callerMethod.Module);
            instructionMap[instruction] = clone;
            rebuiltInstructions.Add(clone);
        }

        IlRelocator.FixupBranchTargets(rebuiltInstructions, instructionMap);

        body.Instructions.Clear();
        body.ExceptionHandlers.Clear();
        var il = body.GetILProcessor();
        foreach (var instruction in rebuiltInstructions)
        {
            il.Append(instruction);
        }

        IlRelocator.RelocateExceptionHandlers(
            body.ExceptionHandlers,
            originalHandlers,
            instructionMap,
            callerMethod.Module);

        foreach (var handler in replacementHandlers)
        {
            body.ExceptionHandlers.Add(handler);
        }

        IlRelocator.ExpandShortBranches(body.Instructions);
        return true;
    }

    private static bool BuildCallSiteReplacement(
        MethodDefinition callerMethod,
        CallSiteWeaveMatch callSite,
        out List<Instruction> instructions,
        out List<ExceptionHandler> handlers,
        out string? error)
    {
        instructions = [];
        handlers = [];
        error = null;

        var module = callerMethod.Module;
        var callSlotTypes = GetCallSlotTypes(callSite.CalledMethod);
        var callSlotLocals = new List<VariableDefinition>(callSlotTypes.Count);
        foreach (var slotType in callSlotTypes)
        {
            var local = new VariableDefinition(module.ImportReference(slotType));
            callerMethod.Body.Variables.Add(local);
            callSlotLocals.Add(local);
        }

        for (var i = callSlotLocals.Count - 1; i >= 0; i--)
        {
            instructions.Add(Instruction.Create(OpCodes.Stloc, callSlotLocals[i]));
        }

        var isNonVoid = !IlTypeHelper.IsVoidReturn(callSite.CalledMethod.ReturnType);
        VariableDefinition? returnLocal = null;
        Instruction finalLabel;
        if (isNonVoid)
        {
            returnLocal = new VariableDefinition(module.ImportReference(callSite.CalledMethod.ReturnType));
            callerMethod.Body.Variables.Add(returnLocal);
            finalLabel = Instruction.Create(OpCodes.Ldloc, returnLocal);
        }
        else
        {
            finalLabel = Instruction.Create(OpCodes.Nop);
        }

        var segment = BuildOriginalCallSegment(callSite, callSlotLocals, returnLocal, module);
        foreach (var weave in callSite.Weaves.Reverse<WeaveInfo>())
        {
            if (!BuildWeaveSegment(
                    weave,
                    callSite,
                    callSlotLocals,
                    returnLocal,
                    finalLabel,
                    segment,
                    callerMethod.Body,
                    module,
                    out segment,
                    out var weaveHandlers,
                    out error))
            {
                return false;
            }

            handlers.AddRange(weaveHandlers);
        }

        instructions.AddRange(segment);
        instructions.Add(finalLabel);
        return true;
    }

    private static List<Instruction> BuildOriginalCallSegment(
        CallSiteWeaveMatch callSite,
        IReadOnlyList<VariableDefinition> callSlotLocals,
        VariableDefinition? returnLocal,
        ModuleDefinition module)
    {
        var result = new List<Instruction>();
        foreach (var local in callSlotLocals)
        {
            result.Add(Instruction.Create(OpCodes.Ldloc, local));
        }

        var importedCall = module.ImportReference(callSite.CalledMethod);
        result.Add(Instruction.Create(callSite.CallInstruction.OpCode, importedCall));
        if (returnLocal != null)
        {
            result.Add(Instruction.Create(OpCodes.Stloc, returnLocal));
        }

        return result;
    }

    private static bool BuildWeaveSegment(
        WeaveInfo weave,
        CallSiteWeaveMatch callSite,
        IReadOnlyList<VariableDefinition> callSlotLocals,
        VariableDefinition? returnLocal,
        Instruction finalLabel,
        List<Instruction> innerSegment,
        MethodBody callerBody,
        ModuleDefinition module,
        out List<Instruction> segment,
        out List<ExceptionHandler> handlers,
        out string? error)
    {
        segment = [];
        handlers = [];
        error = null;

        var weaveMethod = weave.WeaveMethod;
        if (!weaveMethod.HasBody)
        {
            error = $"WeaveCallSite 编织方法 '{weave.WeaveMethodDisplayName}' 无方法体。";
            return false;
        }

        var weaveInstructions = weaveMethod.Body.Instructions.ToList();
        var markerIndex = FindMarkerIndex(weaveInstructions);
        var localMap = new Dictionary<VariableDefinition, VariableDefinition>();
        foreach (var weaveLocal in weaveMethod.Body.Variables)
        {
            var targetLocal = new VariableDefinition(module.ImportReference(weaveLocal.VariableType));
            callerBody.Variables.Add(targetLocal);
            localMap[weaveLocal] = targetLocal;
        }

        var cloneMap = new Dictionary<Instruction, Instruction>();
        var clonesByIndex = new Instruction?[weaveInstructions.Count];
        var returnValueParamIndex = GetReturnValueParamIndex(weaveMethod, callSlotLocals.Count, returnLocal);
        var captureKinds = weave.Pattern.IsWildcard
            ? WeaveCaptureInjector.GetCaptureKinds(weaveMethod)
            : [];
        var hasObjectInstanceSlot = weave.Pattern.IsWildcard
            && WeaveCaptureInjector.HasObjectInstanceSlot(weaveMethod);

        for (var i = 0; i < weaveInstructions.Count; i++)
        {
            if (i == markerIndex)
            {
                continue;
            }

            var clone = CloneWeaveInstruction(
                weaveInstructions[i],
                weaveMethod,
                localMap,
                callSlotLocals,
                returnLocal,
                returnValueParamIndex,
                captureKinds,
                hasObjectInstanceSlot,
                callSite,
                module);

            if (clone.OpCode == OpCodes.Ret)
            {
                clone.OpCode = OpCodes.Br;
                clone.Operand = finalLabel;
            }

            clonesByIndex[i] = clone;
            cloneMap[weaveInstructions[i]] = clone;
        }

        var innerEntry = innerSegment.Count > 0 ? innerSegment[0] : finalLabel;
        if (markerIndex >= 0)
        {
            cloneMap[weaveInstructions[markerIndex]] = innerEntry;
        }

        for (var i = 0; i < weaveInstructions.Count; i++)
        {
            if (i == markerIndex)
            {
                segment.AddRange(innerSegment);
                continue;
            }

            var clone = clonesByIndex[i];
            if (clone != null)
            {
                segment.Add(clone);
            }
        }

        IlRelocator.FixupBranchTargets(segment, cloneMap);
        handlers.AddRange(CloneWeaveHandlers(weaveMethod, weaveInstructions, markerIndex, innerEntry, cloneMap, module));
        return true;
    }

    private static Instruction CloneWeaveInstruction(
        Instruction source,
        MethodDefinition weaveMethod,
        IReadOnlyDictionary<VariableDefinition, VariableDefinition> localMap,
        IReadOnlyList<VariableDefinition> callSlotLocals,
        VariableDefinition? returnLocal,
        int returnValueParamIndex,
        IReadOnlyList<WeaveCaptureKind> captureKinds,
        bool hasObjectInstanceSlot,
        CallSiteWeaveMatch callSite,
        ModuleDefinition module)
    {
        var paramIndex = GetParameterIndex(source);
        if (paramIndex >= 0)
        {
            if (paramIndex < captureKinds.Count)
            {
                var captureKind = captureKinds[paramIndex];
                if (captureKind != WeaveCaptureKind.None)
                {
                    var captureLoad = WeaveCaptureInjector.CreateCaptureLoadInstruction(
                        captureKind,
                        callSite.ResolvedCalledMethod,
                        module);
                    if (captureLoad != null)
                    {
                        return captureLoad;
                    }
                }
            }

            if (paramIndex == 0 && hasObjectInstanceSlot)
            {
                if (callSite.CalledMethod.HasThis && callSlotLocals.Count > 0)
                {
                    return Instruction.Create(OpCodes.Ldloc, callSlotLocals[0]);
                }

                return Instruction.Create(OpCodes.Ldnull);
            }

            if (paramIndex == returnValueParamIndex && returnLocal != null)
            {
                return CreateParameterMappedInstruction(source.OpCode, returnLocal, addressForLoad: true);
            }

            if (paramIndex < callSlotLocals.Count)
            {
                var parameter = weaveMethod.Parameters[paramIndex];
                var loadAddress = parameter.ParameterType.IsByReference || IsLoadAddress(source.OpCode);
                return CreateParameterMappedInstruction(source.OpCode, callSlotLocals[paramIndex], loadAddress);
            }
        }

        if (TryRemapWeaveLocal(source, weaveMethod.Body, localMap, out var remappedLocalInstruction))
        {
            return remappedLocalInstruction;
        }

        return CloneInstruction(source, module);
    }

    private static List<ExceptionHandler> CloneWeaveHandlers(
        MethodDefinition weaveMethod,
        IReadOnlyList<Instruction> weaveInstructions,
        int markerIndex,
        Instruction innerEntry,
        Dictionary<Instruction, Instruction> cloneMap,
        ModuleDefinition module)
    {
        if (markerIndex < 0)
        {
            return [];
        }

        var marker = weaveInstructions[markerIndex];
        foreach (var handler in weaveMethod.Body.ExceptionHandlers)
        {
            if (IsInstructionInTryRegion(marker, handler, weaveInstructions))
            {
                cloneMap[handler.TryStart] = innerEntry;
            }
        }

        var collection = new Collection<ExceptionHandler>();
        IlRelocator.RelocateExceptionHandlers(collection, weaveMethod.Body.ExceptionHandlers, cloneMap, module);
        return collection.ToList();
    }

    private static List<TypeReference> GetCallSlotTypes(MethodReference calledMethod)
    {
        var result = new List<TypeReference>();
        if (calledMethod.HasThis)
        {
            result.Add(calledMethod.DeclaringType);
        }

        foreach (var parameter in calledMethod.Parameters)
        {
            result.Add(parameter.ParameterType);
        }

        return result;
    }

    private static int GetReturnValueParamIndex(
        MethodDefinition weaveMethod,
        int callSlotCount,
        VariableDefinition? returnLocal)
    {
        if (returnLocal == null || weaveMethod.Parameters.Count != callSlotCount + 1)
        {
            return -1;
        }

        var lastParam = weaveMethod.Parameters[weaveMethod.Parameters.Count - 1];
        return lastParam.ParameterType.IsByReference ? lastParam.Index : -1;
    }

    private static int FindMarkerIndex(IReadOnlyList<Instruction> instructions)
    {
        for (var i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode == OpCodes.Call
                && instr.Operand is MethodReference mr
                && mr.Name == SharpWeaverMetadata.OriginalBodyMethod
                && mr.DeclaringType.FullName == SharpWeaverMetadata.WeaveTemplate)
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetParameterIndex(Instruction instruction)
    {
        if (instruction.OpCode == OpCodes.Ldarg_0) return 0;
        if (instruction.OpCode == OpCodes.Ldarg_1) return 1;
        if (instruction.OpCode == OpCodes.Ldarg_2) return 2;
        if (instruction.OpCode == OpCodes.Ldarg_3) return 3;
        if ((instruction.OpCode == OpCodes.Ldarg
                || instruction.OpCode == OpCodes.Ldarg_S
                || instruction.OpCode == OpCodes.Ldarga
                || instruction.OpCode == OpCodes.Ldarga_S
                || instruction.OpCode == OpCodes.Starg
                || instruction.OpCode == OpCodes.Starg_S)
            && instruction.Operand is ParameterDefinition parameter)
        {
            return parameter.Index;
        }

        return -1;
    }

    private static bool IsLoadAddress(OpCode opCode) =>
        opCode == OpCodes.Ldarga || opCode == OpCodes.Ldarga_S;

    private static Instruction CreateParameterMappedInstruction(
        OpCode sourceOpCode,
        VariableDefinition local,
        bool addressForLoad)
    {
        if (sourceOpCode == OpCodes.Starg || sourceOpCode == OpCodes.Starg_S)
        {
            return Instruction.Create(OpCodes.Stloc, local);
        }

        return addressForLoad
            ? Instruction.Create(OpCodes.Ldloca, local)
            : Instruction.Create(OpCodes.Ldloc, local);
    }

    private static bool TryRemapWeaveLocal(
        Instruction source,
        MethodBody weaveBody,
        IReadOnlyDictionary<VariableDefinition, VariableDefinition> localMap,
        out Instruction remapped)
    {
        remapped = Instruction.Create(OpCodes.Nop);
        VariableDefinition? sourceLocal = source.Operand as VariableDefinition;
        if (sourceLocal == null && TryGetShortFormLocalIndex(source.OpCode, out var shortIndex))
        {
            if (shortIndex < weaveBody.Variables.Count)
            {
                sourceLocal = weaveBody.Variables[shortIndex];
            }
        }

        if (sourceLocal == null || !localMap.TryGetValue(sourceLocal, out var mappedLocal))
        {
            return false;
        }

        remapped = CreateRemappedLocalInstruction(source.OpCode, mappedLocal);
        return true;
    }

    private static bool TryGetShortFormLocalIndex(OpCode opCode, out int index)
    {
        index = opCode.Code switch
        {
            Code.Ldloc_0 or Code.Stloc_0 => 0,
            Code.Ldloc_1 or Code.Stloc_1 => 1,
            Code.Ldloc_2 or Code.Stloc_2 => 2,
            Code.Ldloc_3 or Code.Stloc_3 => 3,
            _ => -1,
        };
        return index >= 0;
    }

    private static Instruction CreateRemappedLocalInstruction(OpCode sourceOpCode, VariableDefinition mappedLocal)
    {
        var index = mappedLocal.Index;
        return sourceOpCode.Code switch
        {
            Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3
                or Code.Ldloc_S or Code.Ldloc => CreateLoadLocal(index, mappedLocal),
            Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3
                or Code.Stloc_S or Code.Stloc => CreateStoreLocal(index, mappedLocal),
            Code.Ldloca_S or Code.Ldloca => CreateLoadLocalAddress(index, mappedLocal),
            _ => Instruction.Create(sourceOpCode, mappedLocal),
        };
    }

    private static Instruction CreateLoadLocal(int index, VariableDefinition local)
    {
        return index switch
        {
            0 => Instruction.Create(OpCodes.Ldloc_0),
            1 => Instruction.Create(OpCodes.Ldloc_1),
            2 => Instruction.Create(OpCodes.Ldloc_2),
            3 => Instruction.Create(OpCodes.Ldloc_3),
            <= 255 => Instruction.Create(OpCodes.Ldloc_S, local),
            _ => Instruction.Create(OpCodes.Ldloc, local),
        };
    }

    private static Instruction CreateStoreLocal(int index, VariableDefinition local)
    {
        return index switch
        {
            0 => Instruction.Create(OpCodes.Stloc_0),
            1 => Instruction.Create(OpCodes.Stloc_1),
            2 => Instruction.Create(OpCodes.Stloc_2),
            3 => Instruction.Create(OpCodes.Stloc_3),
            <= 255 => Instruction.Create(OpCodes.Stloc_S, local),
            _ => Instruction.Create(OpCodes.Stloc, local),
        };
    }

    private static Instruction CreateLoadLocalAddress(int index, VariableDefinition local)
    {
        return index <= 255
            ? Instruction.Create(OpCodes.Ldloca_S, local)
            : Instruction.Create(OpCodes.Ldloca, local);
    }

    private static bool IsInstructionInTryRegion(
        Instruction instruction,
        ExceptionHandler handler,
        IReadOnlyList<Instruction> instructions)
    {
        var index = IndexOf(instructions, instruction);
        var tryStart = IndexOf(instructions, handler.TryStart);
        var tryEnd = IndexOf(instructions, handler.TryEnd);
        return index >= tryStart && index < tryEnd;
    }

    private static int IndexOf(IReadOnlyList<Instruction> instructions, Instruction instruction)
    {
        for (var i = 0; i < instructions.Count; i++)
        {
            if (ReferenceEquals(instructions[i], instruction))
            {
                return i;
            }
        }

        return -1;
    }

    private static Instruction CloneInstruction(Instruction source, ModuleDefinition module)
    {
        return source.Operand switch
        {
            null => Instruction.Create(source.OpCode),
            MethodReference mr => Instruction.Create(source.OpCode, module.ImportReference(mr)),
            FieldReference fr => Instruction.Create(source.OpCode, module.ImportReference(fr)),
            TypeReference tr => Instruction.Create(source.OpCode, module.ImportReference(tr)),
            VariableDefinition vd => Instruction.Create(source.OpCode, vd),
            ParameterDefinition pd => Instruction.Create(source.OpCode, pd),
            string s => Instruction.Create(source.OpCode, s),
            int n => Instruction.Create(source.OpCode, n),
            long l => Instruction.Create(source.OpCode, l),
            float f => Instruction.Create(source.OpCode, f),
            double d => Instruction.Create(source.OpCode, d),
            byte b => Instruction.Create(source.OpCode, b),
            sbyte sb => Instruction.Create(source.OpCode, sb),
            Instruction target => Instruction.Create(source.OpCode, target),
            Instruction[] targets => Instruction.Create(source.OpCode, (Instruction[])targets.Clone()),
            CallSite cs => Instruction.Create(source.OpCode, cs),
            _ => throw new NotSupportedException(
                $"WeaveCallSiteSplicer：不支持的指令操作数类型 '{source.Operand.GetType().FullName}'" +
                $"（指令 {source.OpCode}）。"),
        };
    }
}
