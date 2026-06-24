using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver;

/// <summary>
/// Splices an ILWeaving weave method into an override method body.
/// </summary>
/// <remarks>
/// <para><b>Splicing invariants (Round 3):</b></para>
/// <list type="bullet">
///   <item><description>
///     The weave method body must contain exactly one <c>call WeaveTemplate.OriginalBody()</c> marker instruction;
///     more than one is reported as an error by <see cref="WeaveSignatureValidator"/> before splicing.
///   </description></item>
///   <item><description>
///     All <c>ret</c> instructions in the original method body are modified in place to branch instructions:
///     void target → <c>br afterOriginalNop</c>;
///     non-void target → insert <c>stloc returnLocal</c> before the <c>ret</c> clone, then change <c>ret</c> to <c>br afterOriginalNop</c>.
///   </description></item>
///   <item><description>
///     All branches in the weave method that point to the marker instruction are redirected during branch-fixup via <see cref="IlRelocator.FixupBranchTargets"/>
///     to the original method body entry (<c>origBodyEntry</c>), rather than to the dangling marker clone.
///   </description></item>
///   <item><description>
///     <c>ref T</c> parameter alignment: when a weave method parameter type is <c>ref T</c>, the corresponding
///     <c>ldarg</c> instructions in the weave body are automatically remapped to <c>ldarga</c> to match the by-value parameter layout in the override method.
///   </description></item>
///   <item><description>
///     Return slot (trailing <c>ref TReturn returnValue</c>): the last weave method parameter is <c>ref TReturn</c>
///     and the override returns <c>TReturn</c>, <c>ldarg</c> is remapped to <c>ldloca returnLocal</c>;
///     all <c>ret</c> instructions in the weave method are remapped to <c>br finalRetLabel</c>, and finally <c>ldloc returnLocal; ret</c> is emitted.
///   </description></item>
///   <item><description>
///     Internal branch targets in weave instructions are fixed up via <c>weaveEhMap</c> (including marker → origBodyEntry redirection),
///     ensuring internal jumps in prefix/postfix point to the clone, and marker branches point to the original body entry.
///   </description></item>
///   <item><description>
///     Exception handlers from the original method body are cloned and mapped to cloned instructions; weave method exception handlers are also cloned,
///     with boundaries pointing to the marker instruction mapped to the original body entry, while end boundaries remain unchanged.
///   </description></item>
/// </list>
/// </remarks>
public static class WeaveSplicer
{
    /// <summary>
    /// Splices a single weave method into an override method body, replacing the <c>OriginalBody()</c> marker with an inline copy of the original method body.
    /// </summary>
    /// <param name="weave">Weave method information.</param>
    /// <param name="overrideMethod">Override method to be woven (will be modified in place).</param>
    /// <param name="captureMethod">Metadata capture source; defaults to <paramref name="overrideMethod"/>.</param>
    /// <param name="error">Error message on failure.</param>
    /// <returns>Whether the splice succeeded.</returns>
    public static bool TrySplice(
        WeaveInfo weave,
        MethodDefinition overrideMethod,
        MethodDefinition captureMethod,
        out string? error)
    {
        error = null;

        if (!overrideMethod.HasBody)
        {
            error =
                $"override 方法 '{MethodSignatureFormatter.Format(overrideMethod)}' 无方法体，" +
                $"无法应用 ILWeaving 编织 '{weave.WeaveMethodDisplayName}'。";
            return false;
        }

        var weaveMethod = weave.WeaveMethod;
        var isWildcardWeave = WeaveCaptureInjector.IsWildcardWeave(weave);
        var captureKinds = isWildcardWeave
            ? WeaveCaptureInjector.GetCaptureKinds(weaveMethod)
            : Array.Empty<WeaveCaptureKind>();
        var hasObjectInstanceSlot = isWildcardWeave && WeaveCaptureInjector.HasObjectInstanceSlot(weaveMethod);
        var weaveBody = weaveMethod.Body;
        var weaveInstrList = weaveBody.Instructions.ToList();

        var markerIndex = FindMarkerIndex(weaveInstrList);
        if (markerIndex < 0)
        {
            error =
                $"ILWeaving 编织方法 '{weave.WeaveMethodDisplayName}' 中未找到 " +
                $"WeaveTemplate.OriginalBody() 标记调用。";
            return false;
        }

        var overrideBody = overrideMethod.Body;
        var origInstrList = overrideBody.Instructions.ToList();

        // Save original exception handlers before modifying the body.
        var origExHandlers = overrideBody.ExceptionHandlers.ToList();
        var module = overrideMethod.Module;

        // ── Detect return-value slot ─────────────────────────────────────────────────────
        // If the override returns non-void AND the weave has a trailing ref TReturn parameter,
        // set up a local variable to carry the return value through the splice.
        var isNonVoidReturn = !IlTypeHelper.IsVoidReturn(overrideMethod.ReturnType);
        var returnValueParamIdx = -1;
        VariableDefinition? returnLocal = null;
        Instruction? finalRetLabel = null;

        if (isNonVoidReturn && weaveMethod.Parameters.Count > 0)
        {
            var lastParam = weaveMethod.Parameters[weaveMethod.Parameters.Count - 1];
            if (lastParam.ParameterType.IsByReference)
            {
                var elemType = ((ByReferenceType)lastParam.ParameterType).ElementType;
                if (elemType.FullName == overrideMethod.ReturnType.FullName)
                {
                    returnValueParamIdx = lastParam.Index;
                }
            }
        }

        if (isNonVoidReturn && returnLocal == null)
        {
            returnLocal = new VariableDefinition(module.ImportReference(overrideMethod.ReturnType));
            overrideBody.Variables.Add(returnLocal);
            finalRetLabel = Instruction.Create(OpCodes.Ldloc, returnLocal);
        }
        else if (returnValueParamIdx >= 0 && returnLocal == null)
        {
            returnLocal = new VariableDefinition(module.ImportReference(overrideMethod.ReturnType));
            overrideBody.Variables.Add(returnLocal);
            finalRetLabel = Instruction.Create(OpCodes.Ldloc, returnLocal);
        }

        // ── 0. Build ref-param IL-index set ─────────────────────────────────────────────
        // The returnValue slot is handled separately (ldloca); exclude it from the ldarg→ldarga set.
        // Wildcard weaves have no per-target ref parameters.
        var refParamIlIndices = isWildcardWeave
            ? []
            : BuildRefParamIlIndices(weaveMethod, returnValueParamIdx);

        // ── 1. Map weave locals → new VariableDefinitions appended to override's locals ──
        var localMap = new Dictionary<VariableDefinition, VariableDefinition>();
        foreach (var wLocal in weaveBody.Variables)
        {
            var newLocal = new VariableDefinition(module.ImportReference(wLocal.VariableType));
            overrideBody.Variables.Add(newLocal);
            localMap[wLocal] = newLocal;
        }

        var typeParamsCaptureLocal = CreateTypeParamsCaptureLocal(
            overrideBody,
            captureKinds,
            module);
        var typeParamsCaptureInitializers = typeParamsCaptureLocal != null
            ? BuildTypeParamsCaptureInitializers(captureMethod, typeParamsCaptureLocal, module)
            : [];

        // ── 2. Clone all weave instructions (branch fixup deferred — needs origBodyEntry) ─
        var weaveCloneMap = new Dictionary<Instruction, Instruction>(weaveInstrList.Count);
        var weaveClones = new Instruction[weaveInstrList.Count];
        for (var i = 0; i < weaveInstrList.Count; i++)
        {
            weaveClones[i] = CloneWeaveInstruction(
                weaveInstrList[i], module, localMap, weaveMethod,
                refParamIlIndices, returnValueParamIdx, returnLocal,
                overrideMethod, captureKinds, hasObjectInstanceSlot, typeParamsCaptureLocal, captureMethod);
            weaveCloneMap[weaveInstrList[i]] = weaveClones[i];
        }

        // ── 3. Clone original body instructions ──────────────────────────────────────────
        var origCloneMap = new Dictionary<Instruction, Instruction>(origInstrList.Count);
        var origClones = new Instruction[origInstrList.Count];
        for (var i = 0; i < origInstrList.Count; i++)
        {
            origClones[i] = CloneOriginalInstruction(origInstrList[i], module);
            origCloneMap[origInstrList[i]] = origClones[i];
        }

        // ── 4. Build afterOriginalNop and weave EH map ────────────────────────────────────
        // weaveEhMap extends weaveCloneMap with the critical override:
        //   weaveMarker → origBodyEntry
        // This serves dual purpose:
        //   a) Branch fixup: any branch in weave prefix pointing to the marker is redirected
        //      to the first instruction of the inlined original body (not a dropped clone).
        //   b) Exception handler relocation: try-start / try-end boundaries that reference the
        //      marker instruction are remapped to the original body entry.
        var afterOriginalNop = Instruction.Create(OpCodes.Nop);
        var origBodyEntry = origClones.Length > 0 ? origClones[0] : afterOriginalNop;
        var weaveMarker = weaveInstrList[markerIndex];
        var weaveEhMap = new Dictionary<Instruction, Instruction>(weaveCloneMap)
        {
            [weaveMarker] = origBodyEntry,
        };

        // When compiler try/catch wraps OriginalBody(), TryStart may point at a
        // leading nop rather than the marker call itself — remap TryStart to origBodyEntry
        // whenever the marker falls inside the try region.
        foreach (var handler in weaveBody.ExceptionHandlers)
        {
            if (IsInstructionInTryRegion(weaveMarker, handler, weaveInstrList))
            {
                weaveEhMap[handler.TryStart] = origBodyEntry;
            }
        }

        // ── 5. Fix branch targets in weave clones using weaveEhMap ───────────────────────
        // Must happen AFTER origBodyEntry is known so marker-pointing branches resolve
        // to the first real instruction of the inlined original body.
        IlRelocator.FixupBranchTargets(weaveClones, weaveEhMap);

        // ── 6. Fix branch targets in original body clones ────────────────────────────────
        IlRelocator.FixupBranchTargets(origClones, origCloneMap);

        // ── 7. For non-void: redirect weave ret → br finalRetLabel ───────────────────────
        // Weave method is static void; its ret instructions leave the stack empty.
        // In a non-void override, a void-style ret is invalid — redirect to the terminal
        // sequence that loads returnLocal and returns.
        if (isNonVoidReturn && finalRetLabel != null)
        {
            foreach (var clone in weaveClones)
            {
                if (clone.OpCode == OpCodes.Ret)
                {
                    clone.OpCode = OpCodes.Br;
                    clone.Operand = finalRetLabel;
                }
            }
        }
        // ── 8. Replace ret in original clones ────────────────────────────────────────────
        // Void: ret → br afterOriginalNop (current clone modified in-place).
        // Non-void: each ret becomes [stloc returnLocal] + br afterOriginalNop; any branch
        // that targeted ret must land on stloc so converging paths store the return value.
        var retToStlocMap = new Dictionary<Instruction, Instruction>();
        foreach (var clone in origClones)
        {
            if (clone.OpCode != OpCodes.Ret)
            {
                continue;
            }

            if (isNonVoidReturn)
            {
                retToStlocMap[clone] = Instruction.Create(OpCodes.Stloc, returnLocal!);
            }

            clone.OpCode = OpCodes.Br;
            clone.Operand = afterOriginalNop;
        }

        if (retToStlocMap.Count > 0)
        {
            foreach (var clone in origClones)
            {
                RetargetBranchOperands(clone, retToStlocMap);
            }
        }

        // ── 9. Rebuild override body ──────────────────────────────────────────────────────
        overrideBody.Instructions.Clear();
        overrideBody.ExceptionHandlers.Clear();
        var il = overrideBody.GetILProcessor();

        // Prefix: weave instructions before marker
        foreach (var initializer in typeParamsCaptureInitializers)
        {
            il.Append(initializer);
        }

        for (var i = 0; i < markerIndex; i++)
        {
            il.Append(weaveClones[i]);
        }

        // Inlined original body.
        // For non-void: emit stloc returnLocal immediately before each former ret site.
        foreach (var origClone in origClones)
        {
            if (retToStlocMap.TryGetValue(origClone, out var stlocInsn))
            {
                il.Append(stlocInsn);
            }

            il.Append(origClone);
        }

        il.Append(afterOriginalNop);

        // Postfix: weave instructions after marker (marker itself is dropped)
        for (var i = markerIndex + 1; i < weaveInstrList.Count; i++)
        {
            il.Append(weaveClones[i]);
        }

        // For non-void: emit the terminal return sequence that all ret→br chains land on.
        if (isNonVoidReturn && finalRetLabel != null)
        {
            il.Append(finalRetLabel);   // ldloc returnLocal
            il.Append(Instruction.Create(OpCodes.Ret));
        }

        // ── 10. Relocate exception handlers ──────────────────────────────────────────────
        // Original override handlers map through origCloneMap.
        IlRelocator.RelocateExceptionHandlers(
            overrideBody.ExceptionHandlers, origExHandlers, origCloneMap, module);

        // Weave method handlers map through weaveEhMap (marker → origBodyEntry).
        IlRelocator.RelocateExceptionHandlers(
            overrideBody.ExceptionHandlers, weaveBody.ExceptionHandlers, weaveEhMap, module);

        IlRelocator.ExpandShortBranches(overrideBody.Instructions);

        return true;
    }

    /// <summary>
    /// Builds the set of parameter indices in the weave method that require <c>ldarg → ldarga</c> remapping (excluding the returnValue slot).
    /// </summary>
    private static HashSet<int> BuildRefParamIlIndices(MethodDefinition weaveMethod, int excludeIdx)
    {
        var result = new HashSet<int>();
        for (var i = 0; i < weaveMethod.Parameters.Count; i++)
        {
            if (i == excludeIdx)
            {
                continue;
            }

            if (weaveMethod.Parameters[i].ParameterType.IsByReference)
            {
                result.Add(i);
            }
        }

        return result;
    }

    private static int FindMarkerIndex(List<Instruction> instructions)
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

    /// <summary>
    /// Replaces instruction operands that are keys in <paramref name="replacementMap"/> with their corresponding values.
    /// </summary>
    private static void RetargetBranchOperands(
        Instruction instruction,
        IReadOnlyDictionary<Instruction, Instruction> replacementMap)
    {
        if (instruction.Operand is Instruction branchTarget
            && replacementMap.TryGetValue(branchTarget, out var newTarget))
        {
            instruction.Operand = newTarget;
        }
        else if (instruction.Operand is Instruction[] switchTargets)
        {
            for (var i = 0; i < switchTargets.Length; i++)
            {
                if (replacementMap.TryGetValue(switchTargets[i], out var newSwitchTarget))
                {
                    switchTargets[i] = newSwitchTarget;
                }
            }
        }
    }

    /// <summary>
    /// Clones an instruction from the original override method body.
    /// </summary>
    private static Instruction CloneOriginalInstruction(Instruction source, ModuleDefinition module)
    {
        return source.Operand switch
        {
            null => Instruction.Create(source.OpCode),
            MethodReference mr => Instruction.Create(source.OpCode, mr),
            FieldReference fr => Instruction.Create(source.OpCode, fr),
            TypeReference tr => Instruction.Create(source.OpCode, tr),
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
                $"WeaveSplicer：不支持的原始指令操作数类型 '{source.Operand.GetType().FullName}'" +
                $"（指令 {source.OpCode}）。"),
        };
    }

    /// <summary>
    /// Clones an instruction from the weave method body, applying the following remappings:
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>VariableDefinition</c> operands → override local variables from <paramref name="localMap"/>.
    ///   </description></item>
    ///   <item><description>
    ///     <c>ldarg</c> for the returnValue slot (<paramref name="returnValueParamIdx"/>) →
    ///     <c>ldloca returnLocal</c> (loads the address of the override's return value local).
    ///   </description></item>
    ///   <item><description>
    ///     Other <c>ldarg</c> for <c>ref T</c> parameter slots (<paramref name="refParamIlIndices"/>) →
    ///     <c>ldarga</c> (loads the override parameter address).
    ///   </description></item>
    /// </list>
    /// </summary>
    private static Instruction CloneWeaveInstruction(
        Instruction source,
        ModuleDefinition module,
        Dictionary<VariableDefinition, VariableDefinition> localMap,
        MethodDefinition weaveMethod,
        HashSet<int> refParamIlIndices,
        int returnValueParamIdx,
        VariableDefinition? returnLocal,
        MethodDefinition overrideMethod,
        WeaveCaptureKind[] captureKinds,
        bool hasObjectInstanceSlot,
        VariableDefinition? typeParamsCaptureLocal,
        MethodDefinition captureMethod)
    {
        var ilArgIndex = GetArgIlIndex(source);

        if (ilArgIndex >= 0 && captureKinds.Length > ilArgIndex)
        {
            var captureKind = captureKinds[ilArgIndex];
            if (captureKind != WeaveCaptureKind.None)
            {
                if (captureKind == WeaveCaptureKind.TypeParams && typeParamsCaptureLocal != null)
                {
                    return Instruction.Create(OpCodes.Ldloc, typeParamsCaptureLocal);
                }

                var captureLoad = WeaveCaptureInjector.CreateCaptureLoadInstruction(
                    captureKind, captureMethod, module);
                if (captureLoad != null)
                {
                    return captureLoad;
                }
            }
        }

        // Regex object? instance slot on static target: ldarg → ldnull
        if (ilArgIndex == 0 && hasObjectInstanceSlot && !captureMethod.HasThis)
        {
            return Instruction.Create(OpCodes.Ldnull);
        }

        // ── Return-value slot: ldarg → ldloca returnLocal ─────────────────────────────────
        if (ilArgIndex >= 0 && ilArgIndex == returnValueParamIdx && returnLocal != null)
        {
            return Instruction.Create(OpCodes.Ldloca_S, returnLocal);
        }

        // ── Other ref params: ldarg → ldarga ──────────────────────────────────────────────
        if (ilArgIndex >= 0 && refParamIlIndices.Contains(ilArgIndex))
        {
            ParameterDefinition paramDef;
            if (source.Operand is ParameterDefinition pd)
            {
                paramDef = pd;
            }
            else
            {
                paramDef = weaveMethod.Parameters[ilArgIndex];
            }

            return Instruction.Create(OpCodes.Ldarga_S, paramDef);
        }

        // ── Remap weave locals (short-form ldloc/stloc/ldloca included) ───────────────────
        if (TryRemapWeaveLocal(source, weaveMethod.Body, localMap, out var remappedLocal))
        {
            return remappedLocal;
        }

        // ── Default clone ─────────────────────────────────────────────────────────────────
        return source.Operand switch
        {
            null => Instruction.Create(source.OpCode),
            MethodReference mr => Instruction.Create(source.OpCode, module.ImportReference(mr)),
            FieldReference fr => Instruction.Create(source.OpCode, module.ImportReference(fr)),
            TypeReference tr => Instruction.Create(source.OpCode, module.ImportReference(tr)),
            VariableDefinition vd2 => Instruction.Create(source.OpCode, vd2),
            ParameterDefinition pd2 => Instruction.Create(source.OpCode, pd2),
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
                $"WeaveSplicer：不支持的编织指令操作数类型 '{source.Operand.GetType().FullName}'" +
                $"（指令 {source.OpCode}）。"),
        };
    }

    /// <summary>
    /// Determines whether <paramref name="instruction"/> is located within the try region of an exception handler (TryEnd is exclusive upper bound).
    /// </summary>
    private static bool IsInstructionInTryRegion(
        Instruction instruction,
        ExceptionHandler handler,
        List<Instruction> instructions)
    {
        var index = instructions.IndexOf(instruction);
        var tryStart = instructions.IndexOf(handler.TryStart);
        var tryEnd = instructions.IndexOf(handler.TryEnd);
        return index >= tryStart && index < tryEnd;
    }

    /// <summary>
    /// Remaps local variable access instructions in the weave method body (including short forms <c>ldloc.0</c> / <c>stloc.1</c> etc.)
    /// to the corresponding cloned local variables in the override.
    /// </summary>
    private static bool TryRemapWeaveLocal(
        Instruction source,
        MethodBody weaveBody,
        IReadOnlyDictionary<VariableDefinition, VariableDefinition> localMap,
        out Instruction remapped)
    {
        remapped = null!;

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

    /// <summary>
    /// Returns the IL parameter index of <c>ldarg</c>-like instructions; returns <c>-1</c> for non-<c>ldarg</c> instructions.
    /// </summary>
    private static int GetArgIlIndex(Instruction instr)
    {
        if (instr.OpCode == OpCodes.Ldarg_0) return 0;
        if (instr.OpCode == OpCodes.Ldarg_1) return 1;
        if (instr.OpCode == OpCodes.Ldarg_2) return 2;
        if (instr.OpCode == OpCodes.Ldarg_3) return 3;
        if ((instr.OpCode == OpCodes.Ldarg_S || instr.OpCode == OpCodes.Ldarg)
            && instr.Operand is ParameterDefinition pd)
        {
            return pd.Index;
        }

        return -1;
    }

    private static VariableDefinition? CreateTypeParamsCaptureLocal(
        MethodBody targetBody,
        IReadOnlyList<WeaveCaptureKind> captureKinds,
        ModuleDefinition module)
    {
        if (!captureKinds.Contains(WeaveCaptureKind.TypeParams))
        {
            return null;
        }

        var local = new VariableDefinition(new ArrayType(module.ImportReference(typeof(Type))));
        targetBody.Variables.Add(local);
        return local;
    }

    private static Instruction[] BuildTypeParamsCaptureInitializers(
        MethodDefinition captureMethod,
        VariableDefinition typeParamsCaptureLocal,
        ModuleDefinition module)
    {
        var instructions = new List<Instruction>(
            WeaveCaptureInjector.CreateTypeParamsArrayLoadInstructions(captureMethod, module))
        {
            Instruction.Create(OpCodes.Stloc, typeParamsCaptureLocal),
        };

        return instructions.ToArray();
    }
}
