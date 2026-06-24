using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver;

/// <summary>Splices the AsyncILWeaving weave template into the target async state machine <c>MoveNext</c>.</summary>
public static class AsyncWeaveSplicer
{
    /// <summary>
    /// Splices the async weave template into the target <c>MoveNext</c>: inserts the prefix into the initial working area and appends the postfix after the final completion path.
    /// </summary>
    /// <param name="weave">Weave information.</param>
    /// <param name="targetMoveNext">Target state machine <c>MoveNext</c> (will be modified in place).</param>
    /// <param name="outerMethod">User-visible outer method (source for metadata capture).</param>
    /// <param name="error">Error message on failure.</param>
    /// <returns>Whether the splice succeeded.</returns>
    public static bool TrySplice(
        WeaveInfo weave,
        MethodDefinition targetMoveNext,
        MethodDefinition outerMethod,
        out string? error)
    {
        error = null;

        AsyncMethodHelper.StripAsyncMethodBodyDebugInfo(targetMoveNext);

        try
        {
            return TrySpliceCore(weave, targetMoveNext, outerMethod, out error);
        }
        catch (Exception ex)
        {
            error =
                $"AsyncILWeaving 编织 '{weave.WeaveMethodDisplayName}' 到 " +
                $"'{outerMethod.DeclaringType.FullName}.{outerMethod.Name}' 失败：{ex.Message}";
            return false;
        }
    }

    private static bool TrySpliceCore(
        WeaveInfo weave,
        MethodDefinition targetMoveNext,
        MethodDefinition outerMethod,
        out string? error)
    {
        error = null;

        if (!targetMoveNext.HasBody)
        {
            error = $"目标 MoveNext '{MethodSignatureFormatter.Format(targetMoveNext)}' 无方法体。";
            return false;
        }

        if (!AsyncMethodHelper.TryResolveMoveNext(weave.WeaveMethod, out var templateMoveNext, out var templateStateMachine))
        {
            error = $"AsyncILWeaving 编织 '{weave.WeaveMethodDisplayName}' 无法解析模板 MoveNext。";
            return false;
        }

        if (!AsyncMethodHelper.TryResolveMoveNext(outerMethod, out _, out var targetStateMachine))
        {
            error = $"目标方法 '{MethodSignatureFormatter.Format(outerMethod)}' 无法解析 MoveNext。";
            return false;
        }

        if (!AsyncAwaitMarkerLocator.TryLocate(templateMoveNext, out var templateBounds, out var locateError))
        {
            error = $"AsyncILWeaving 编织 '{weave.WeaveMethodDisplayName}'：{locateError}";
            return false;
        }

        if (!AsyncIlTypeRewriter.TryCreate(
                outerMethod,
                templateStateMachine,
                targetStateMachine,
                targetMoveNext.Module,
                out var typeRewriter,
                out var rewriterError))
        {
            error = $"AsyncILWeaving 编织 '{weave.WeaveMethodDisplayName}'：{rewriterError}";
            return false;
        }

        var fieldMap = typeRewriter.MergeHoistedFields(templateStateMachine, targetStateMachine);
        var asyncFiberScopeField = FindAsyncFiberMethodScopeField(fieldMap.Values);

        var weaveMethod = weave.WeaveMethod;
        var isWildcardWeave = WeaveCaptureInjector.IsWildcardWeave(weave);
        var captureKinds = isWildcardWeave
            ? WeaveCaptureInjector.GetCaptureKinds(weaveMethod)
            : Array.Empty<WeaveCaptureKind>();
        var hasObjectInstanceSlot = isWildcardWeave && WeaveCaptureInjector.HasObjectInstanceSlot(weaveMethod);

        var templateBody = templateMoveNext.Body;
        var templateInstr = templateBody.Instructions.ToList();
        var targetBody = targetMoveNext.Body;
        var origInstr = targetBody.Instructions.ToList();
        var origExHandlers = targetBody.ExceptionHandlers.ToList();
        var module = targetMoveNext.Module;

        var prefixStart = templateBounds.InitialStateWorkStartIndex;
        var prefixEnd = templateBounds.MarkerCallIndex;
        var suffixStart = AsyncAwaitMarkerLocator.FindTemplateUserSuffixStart(templateMoveNext, templateBounds.MarkerCallIndex, templateBounds.AwaitBlockEndIndex);
        var suffixEnd = AsyncAwaitMarkerLocator.FindTemplateUserSuffixEnd(templateMoveNext, suffixStart);

        var localMap = new Dictionary<VariableDefinition, VariableDefinition>();
        foreach (var templateLocal in templateBody.Variables)
        {
            var newLocal = new VariableDefinition(module.ImportReference(templateLocal.VariableType));
            targetBody.Variables.Add(newLocal);
            localMap[templateLocal] = newLocal;
        }

        var prefixClones = CloneInstructionRange(
            templateInstr,
            prefixStart,
            prefixEnd,
            templateBody,
            module,
            localMap,
            fieldMap,
            templateStateMachine,
            targetStateMachine,
            captureKinds,
            outerMethod,
            typeRewriter);
        var captureInitializers = BuildCaptureInitializers(
            weaveMethod,
            templateStateMachine,
            targetStateMachine,
            fieldMap,
            captureKinds,
            hasObjectInstanceSlot,
            outerMethod,
            module,
            templateInstr,
            prefixStart,
            prefixEnd,
            suffixStart,
            suffixEnd);
        var templateStateInitializers = BuildTemplateStateLocalInitializers(templateBody, localMap);

        var suffixClones = CloneInstructionRange(
            templateInstr,
            suffixStart,
            suffixEnd,
            templateBody,
            module,
            localMap,
            fieldMap,
            templateStateMachine,
            targetStateMachine,
            captureKinds,
            outerMethod,
            typeRewriter);

        var targetWorkStart = AsyncAwaitMarkerLocator.FindInitialStateWorkStartIndex(origInstr);
        var origCloneMap = new Dictionary<Instruction, Instruction>(origInstr.Count);
        var origClones = new Instruction[origInstr.Count];
        for (var i = 0; i < origInstr.Count; i++)
        {
            origClones[i] = CloneOriginalInstruction(origInstr[i]);
            origCloneMap[origInstr[i]] = origClones[i];
        }

        if (targetWorkStart < 0 || targetWorkStart > origClones.Length)
        {
            error =
                $"目标 '{outerMethod.DeclaringType.FullName}.{outerMethod.Name}' 的初始工作区索引无效：{targetWorkStart}。";
            return false;
        }

        var targetWorkEntryIndex = targetWorkStart;
        var completionEpilogueStart = AsyncAwaitMarkerLocator.FindCompletionEpilogueStart(targetMoveNext);
        if (completionEpilogueStart < 0 || completionEpilogueStart >= origClones.Length)
        {
            error =
                $"目标 '{outerMethod.DeclaringType.FullName}.{outerMethod.Name}' 无法定位 async 完成收尾块。";
            return false;
        }

        var catchHandlerStart = Math.Clamp(FindCompilerCatchHandlerStart(targetMoveNext), 0, origClones.Length);

        IlRelocator.FixupBranchTargets(origClones, origCloneMap);

        var suffixEntry = suffixClones.Length > 0 ? suffixClones[0] : null;
        var epilogueEntry = completionEpilogueStart < origClones.Length
            ? origClones[completionEpilogueStart]
            : suffixEntry ?? origClones[^1];
        var completionRedirectTarget = suffixEntry ?? epilogueEntry;

        if (completionEpilogueStart < origInstr.Count)
        {
            var epilogueStartClone = origClones[completionEpilogueStart];

            for (var i = 0; i < completionEpilogueStart; i++)
            {
                var clone = origClones[i];
                if (clone.Operand is not Instruction target
                    || !ReferenceEquals(target, epilogueStartClone))
                {
                    continue;
                }

                clone.Operand = completionRedirectTarget;
            }

            for (var i = 0; i < catchHandlerStart; i++)
            {
                var clone = origClones[i];
                if (clone.OpCode != OpCodes.Leave && clone.OpCode != OpCodes.Leave_S)
                {
                    continue;
                }

                if (clone.Operand is Instruction leaveTarget && ReferenceEquals(leaveTarget, epilogueStartClone))
                {
                    clone.Operand = completionRedirectTarget;
                }
            }
        }

        var completionReturns = AsyncAwaitMarkerLocator.FindFinalCompletionReturns(targetMoveNext);
        foreach (var ret in completionReturns)
        {
            var retIndex = origInstr.IndexOf(ret);
            if (retIndex >= completionEpilogueStart)
            {
                continue;
            }

            if (origCloneMap.TryGetValue(ret, out var clone))
            {
                if (ReferenceEquals(clone, completionRedirectTarget))
                {
                    continue;
                }

                clone.OpCode = OpCodes.Br;
                clone.Operand = completionRedirectTarget;
            }
        }

        foreach (var clone in origClones)
        {
            RetargetBranchOperands(clone, origCloneMap);
        }

        if (suffixClones.Length > 0)
        {
            RedirectSuffixExitsToEpilogue(suffixClones, epilogueEntry);
        }

        var prefixMap = new Dictionary<Instruction, Instruction>();
        for (var i = 0; i < prefixClones.Length; i++)
        {
            prefixMap[templateInstr[prefixStart + i]] = prefixClones[i];
        }

        var suffixMap = new Dictionary<Instruction, Instruction>();
        for (var i = 0; i < suffixClones.Length; i++)
        {
            suffixMap[templateInstr[suffixStart + i]] = suffixClones[i];
        }

        IlRelocator.FixupBranchTargets(prefixClones, prefixMap);
        IlRelocator.FixupBranchTargets(suffixClones, suffixMap);

        var targetWorkEntry = origClones[targetWorkEntryIndex];
        if (captureInitializers.Length > 0 || templateStateInitializers.Length > 0 || prefixClones.Length > 0)
        {
            var prefixEntry = captureInitializers.Length > 0
                ? captureInitializers[0]
                : templateStateInitializers.Length > 0
                    ? templateStateInitializers[0]
                    : prefixClones[0];
            for (var i = 0; i < targetWorkEntryIndex; i++)
            {
                RetargetBranchOperandIfMatches(origClones[i], targetWorkEntry, prefixEntry);
            }
        }

        var prefixBranchMap = BuildTemplateAnchorMap(
            templateInstr,
            templateBounds.MarkerCallIndex,
            templateBounds.AwaitBlockEndIndex,
            targetWorkEntry,
            prefixMap);
        IlRelocator.FixupBranchTargets(prefixClones, prefixBranchMap);

        var suffixBranchMap = BuildTemplateAnchorMap(
            templateInstr,
            templateBounds.MarkerCallIndex,
            templateBounds.AwaitBlockEndIndex,
            targetWorkEntry,
            suffixMap);
        IlRelocator.FixupBranchTargets(suffixClones, suffixBranchMap);

        targetBody.Instructions.Clear();
        targetBody.ExceptionHandlers.Clear();
        var il = targetBody.GetILProcessor();
        var prefixEntryForDispatch = captureInitializers.Length > 0
            ? captureInitializers[0]
            : templateStateInitializers.Length > 0
                ? templateStateInitializers[0]
                : prefixClones.Length > 0
                    ? prefixClones[0]
                    : targetWorkEntry;
        var protectedStart = TryFindCompilerCatchTryStart(origInstr, origExHandlers, origCloneMap, out var compilerTryStart)
            ? compilerTryStart
            : prefixEntryForDispatch;
        var disposeFinally = BuildDisposeFinallyInstructions(
            targetStateMachine,
            fieldMap.Values,
            module);

        AppendCloneRange(
            il,
            BuildAsyncFiberActivationInstructions(targetStateMachine, asyncFiberScopeField, module),
            "async-fiber-activate");
        AppendOriginalRange(il, origClones, 0, targetWorkStart, "dispatch");
        AppendCloneRange(il, captureInitializers, "captures");
        AppendCloneRange(il, templateStateInitializers, "template-state");
        AppendCloneRange(il, prefixClones, "prefix");
        AppendOriginalRange(il, origClones, targetWorkStart, catchHandlerStart, "work");
        AppendCloneRange(il, disposeFinally, "async-dispose");
        AppendOriginalRange(il, origClones, catchHandlerStart, completionEpilogueStart, "catch");
        AppendCloneRange(il, suffixClones, "suffix");
        AppendOriginalRange(il, origClones, completionEpilogueStart, origClones.Length, "epilogue");

        var combinedMap = new Dictionary<Instruction, Instruction>(origCloneMap);
        foreach (var pair in prefixMap)
        {
            combinedMap[pair.Key] = pair.Value;
        }

        foreach (var pair in suffixMap)
        {
            combinedMap[pair.Key] = pair.Value;
        }

        IlRelocator.RelocateExceptionHandlers(
            targetBody.ExceptionHandlers,
            origExHandlers,
            combinedMap,
            module);

        IlRelocator.FixupBranchTargets(targetBody.Instructions, combinedMap);

        if (suffixEntry != null)
        {
            RedirectPreSuffixCompletionsToSuffix(targetBody, epilogueEntry, suffixEntry);
            RedirectHandlersEndingAtEpilogue(targetBody, epilogueEntry, suffixEntry);
        }

        if (disposeFinally.Length > 0)
        {
            var disposeHandlerEnd = catchHandlerStart < origClones.Length
                ? origClones[catchHandlerStart]
                : suffixEntry ?? epilogueEntry;
            RedirectHandlersEndingAt(targetBody, disposeHandlerEnd, disposeFinally[0]);
            var insertIndex = FindHandlerStartingAt(targetBody, disposeHandlerEnd);
            targetBody.ExceptionHandlers.Insert(insertIndex, new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = protectedStart,
                TryEnd = disposeFinally[0],
                HandlerStart = disposeFinally[0],
                HandlerEnd = disposeHandlerEnd,
            });
        }

        ConvertInternalLeavesToBranches(targetBody);
        InjectAsyncFiberDeactivationBeforeReturns(targetBody, targetStateMachine, asyncFiberScopeField, module);
        IlRelocator.ExpandShortBranches(targetBody.Instructions);

        return true;
    }

    private static Instruction[] BuildTemplateStateLocalInitializers(
        MethodBody templateBody,
        IReadOnlyDictionary<VariableDefinition, VariableDefinition> localMap)
    {
        if (templateBody.Variables.Count == 0)
        {
            return [];
        }

        var templateStateLocal = templateBody.Variables[0];
        if (templateStateLocal.VariableType.MetadataType != MetadataType.Int32
            || !localMap.TryGetValue(templateStateLocal, out var mappedStateLocal))
        {
            return [];
        }

        return
        [
            Instruction.Create(OpCodes.Ldc_I4_M1),
            Instruction.Create(OpCodes.Stloc, mappedStateLocal),
        ];
    }

    private static Instruction[] BuildDisposeFinallyInstructions(
        TypeDefinition targetStateMachine,
        IEnumerable<FieldDefinition> mappedFields,
        ModuleDefinition module)
    {
        var stateField = targetStateMachine.Fields.FirstOrDefault(field => field.Name == "<>1__state");
        if (stateField == null)
        {
            return [];
        }

        var disposeFields = mappedFields
            .Where(field => field.DeclaringType.FullName == targetStateMachine.FullName
                && IsDisposableValueTypeField(field))
            .OrderByDescending(field => targetStateMachine.Fields.IndexOf(field))
            .ToList();
        if (disposeFields.Count == 0)
        {
            return [];
        }

        var endFinally = Instruction.Create(OpCodes.Endfinally);
        var instructions = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, ImportStateMachineFieldReference(targetStateMachine, stateField, module)),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Bge, endFinally),
        };

        var disposeMethod = module.ImportReference(typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!);
        foreach (var field in disposeFields)
        {
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(
                OpCodes.Ldflda,
                ImportStateMachineFieldReference(targetStateMachine, field, module)));
            instructions.Add(Instruction.Create(OpCodes.Constrained, module.ImportReference(field.FieldType)));
            instructions.Add(Instruction.Create(OpCodes.Callvirt, disposeMethod));
            instructions.Add(Instruction.Create(OpCodes.Nop));
        }

        instructions.Add(endFinally);
        return instructions.ToArray();
    }

    private static FieldDefinition? FindAsyncFiberMethodScopeField(IEnumerable<FieldDefinition> mappedFields) =>
        mappedFields.FirstOrDefault(field =>
            field.FieldType.Name == "AsyncFiberMethodScope"
            && field.FieldType.DeclaringType?.Name == "TracyProfiler");

    private static Instruction[] BuildAsyncFiberActivationInstructions(
        TypeDefinition targetStateMachine,
        FieldDefinition? asyncFiberScopeField,
        ModuleDefinition module)
    {
        if (asyncFiberScopeField == null)
        {
            return [];
        }

        return
        [
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(
                OpCodes.Ldfld,
                ImportStateMachineFieldReference(targetStateMachine, asyncFiberScopeField, module)),
            Instruction.Create(
                OpCodes.Call,
                ImportAsyncFiberScopeMethod(asyncFiberScopeField, module, "ActivateAsyncFiberMethod")),
        ];
    }

    private static void InjectAsyncFiberDeactivationBeforeReturns(
        MethodBody targetBody,
        TypeDefinition targetStateMachine,
        FieldDefinition? asyncFiberScopeField,
        ModuleDefinition module)
    {
        if (asyncFiberScopeField == null)
        {
            return;
        }

        var returns = targetBody.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ret)
            .ToList();
        if (returns.Count == 0)
        {
            return;
        }

        var fieldReference = ImportStateMachineFieldReference(targetStateMachine, asyncFiberScopeField, module);
        var deactivateMethod = ImportAsyncFiberScopeMethod(asyncFiberScopeField, module, "DeactivateAsyncFiberMethod");
        var il = targetBody.GetILProcessor();

        foreach (var ret in returns)
        {
            var first = Instruction.Create(OpCodes.Ldarg_0);
            var loadScope = Instruction.Create(OpCodes.Ldfld, fieldReference);
            var deactivate = Instruction.Create(OpCodes.Call, deactivateMethod);

            il.InsertBefore(ret, first);
            il.InsertBefore(ret, loadScope);
            il.InsertBefore(ret, deactivate);

            foreach (var instruction in targetBody.Instructions)
            {
                RetargetBranchOperandIfMatches(instruction, ret, first);
            }
        }
    }

    private static MethodReference ImportAsyncFiberScopeMethod(
        FieldDefinition asyncFiberScopeField,
        ModuleDefinition module,
        string methodName)
    {
        var scopeType = asyncFiberScopeField.FieldType.Resolve();
        var profilerType = scopeType.DeclaringType
            ?? throw new InvalidOperationException(
                $"Async fiber scope type '{scopeType.FullName}' does not have a declaring profiler type.");
        var method = profilerType.Methods.FirstOrDefault(candidate =>
                candidate.Name == methodName
                && candidate.Parameters.Count == 1
                && candidate.Parameters[0].ParameterType.Name == "AsyncFiberMethodScope")
            ?? throw new InvalidOperationException(
                $"Could not find TracyProfiler.{methodName}(AsyncFiberMethodScope).");

        return module.ImportReference(method);
    }

    private static bool IsDisposableValueTypeField(FieldDefinition field)
    {
        if (!field.FieldType.IsValueType)
        {
            return false;
        }

        TypeDefinition? typeDefinition;
        try
        {
            typeDefinition = field.FieldType.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return false;
        }

        return typeDefinition?.Interfaces.Any(interfaceImplementation =>
            interfaceImplementation.InterfaceType.FullName == typeof(IDisposable).FullName) == true;
    }

    private static Instruction[] BuildCaptureInitializers(
        MethodDefinition weaveMethod,
        TypeDefinition templateStateMachine,
        TypeDefinition targetStateMachine,
        IReadOnlyDictionary<FieldDefinition, FieldDefinition> fieldMap,
        IReadOnlyList<WeaveCaptureKind> captureKinds,
        bool hasObjectInstanceSlot,
        MethodDefinition outerMethod,
        ModuleDefinition module,
        IReadOnlyList<Instruction> templateInstructions,
        int prefixStart,
        int prefixEnd,
        int suffixStart,
        int suffixEnd)
    {
        var instructions = new List<Instruction>();
        for (var parameterIndex = 0; parameterIndex < weaveMethod.Parameters.Count; parameterIndex++)
        {
            var parameter = weaveMethod.Parameters[parameterIndex];
            var isInstanceSlot = parameterIndex == 0 && hasObjectInstanceSlot;
            var captureKind = parameterIndex < captureKinds.Count
                ? captureKinds[parameterIndex]
                : WeaveCaptureKind.None;
            if (!isInstanceSlot && captureKind == WeaveCaptureKind.None)
            {
                continue;
            }

            var templateField = FindStateMachineField(templateStateMachine, parameter.Name);
            if (templateField == null || !fieldMap.TryGetValue(templateField, out var targetField))
            {
                continue;
            }

            if (!IsTemplateFieldReferencedInRanges(
                    templateField,
                    templateInstructions,
                    prefixStart,
                    prefixEnd,
                    suffixStart,
                    suffixEnd))
            {
                continue;
            }

            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            if (isInstanceSlot)
            {
                AddInstanceLoad(instructions, targetStateMachine, outerMethod, module);
            }
            else
            {
                if (captureKind == WeaveCaptureKind.TypeParams)
                {
                    instructions.AddRange(WeaveCaptureInjector.CreateAsyncTypeParamsArrayLoadInstructions(
                        outerMethod,
                        targetStateMachine,
                        module));
                    instructions.Add(Instruction.Create(
                        OpCodes.Stfld,
                        ImportStateMachineFieldReference(targetStateMachine, targetField, module)));
                    continue;
                }

                var captureLoad = WeaveCaptureInjector.CreateCaptureLoadInstruction(captureKind, outerMethod, module);
                if (captureLoad == null)
                {
                    instructions.RemoveAt(instructions.Count - 1);
                    continue;
                }

                instructions.Add(captureLoad);
            }

            instructions.Add(Instruction.Create(
                OpCodes.Stfld,
                ImportStateMachineFieldReference(targetStateMachine, targetField, module)));
        }

        return instructions.ToArray();
    }

    private static bool IsTemplateFieldReferencedInRanges(
        FieldDefinition templateField,
        IReadOnlyList<Instruction> templateInstructions,
        int prefixStart,
        int prefixEnd,
        int suffixStart,
        int suffixEnd)
    {
        return IsTemplateFieldReferencedInRange(templateField, templateInstructions, prefixStart, prefixEnd)
            || IsTemplateFieldReferencedInRange(templateField, templateInstructions, suffixStart, suffixEnd);
    }

    private static bool IsTemplateFieldReferencedInRange(
        FieldDefinition templateField,
        IReadOnlyList<Instruction> templateInstructions,
        int startInclusive,
        int endExclusive)
    {
        for (var i = Math.Max(0, startInclusive); i < Math.Min(templateInstructions.Count, endExclusive); i++)
        {
            if (templateInstructions[i].Operand is not FieldReference fieldReference)
            {
                continue;
            }

            if (fieldReference.Name == templateField.Name
                && fieldReference.Resolve() == templateField)
            {
                return true;
            }
        }

        return false;
    }

    private static FieldDefinition? FindStateMachineField(TypeDefinition stateMachine, string fieldName)
    {
        foreach (var field in stateMachine.Fields)
        {
            if (field.Name == fieldName)
            {
                return field;
            }
        }

        return null;
    }

    private static void AddInstanceLoad(
        ICollection<Instruction> instructions,
        TypeDefinition targetStateMachine,
        MethodDefinition outerMethod,
        ModuleDefinition module)
    {
        if (!outerMethod.HasThis)
        {
            instructions.Add(Instruction.Create(OpCodes.Ldnull));
            return;
        }

        var thisField = FindStateMachineField(targetStateMachine, "<>4__this")
            ?? targetStateMachine.Fields.FirstOrDefault(field =>
                field.FieldType.FullName == outerMethod.DeclaringType.FullName);
        if (thisField == null)
        {
            instructions.Add(Instruction.Create(OpCodes.Ldnull));
            return;
        }

        instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        instructions.Add(Instruction.Create(OpCodes.Ldfld, module.ImportReference(thisField)));
        if (outerMethod.DeclaringType.IsValueType)
        {
            instructions.Add(Instruction.Create(OpCodes.Box, module.ImportReference(outerMethod.DeclaringType)));
        }
    }

    private static void RedirectPreSuffixCompletionsToSuffix(
        MethodBody targetBody,
        Instruction epilogueEntry,
        Instruction suffixEntry)
    {
        var suffixIndex = targetBody.Instructions.IndexOf(suffixEntry);
        for (var i = 0; i < suffixIndex; i++)
        {
            var instruction = targetBody.Instructions[i];
            if (i == suffixIndex - 1
                && (instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S))
            {
                if (IsInTryRegion(targetBody, i))
                {
                    instruction.Operand = suffixEntry;
                }

                continue;
            }

            if (instruction.Operand is Instruction target
                && ReferenceEquals(target, epilogueEntry))
            {
                if (IsInTryRegion(targetBody, i))
                {
                    instruction.Operand = suffixEntry;
                }

                continue;
            }

            if ((instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S)
                && instruction.Operand is Instruction externalTarget
                && !targetBody.Instructions.Contains(externalTarget)
                && IsInTryRegion(targetBody, i))
            {
                instruction.Operand = suffixEntry;
            }
        }
    }

    private static int FindHandlerStartingAt(MethodBody targetBody, Instruction handlerStart)
    {
        for (var i = 0; i < targetBody.ExceptionHandlers.Count; i++)
        {
            if (ReferenceEquals(targetBody.ExceptionHandlers[i].HandlerStart, handlerStart))
            {
                return i;
            }
        }

        return targetBody.ExceptionHandlers.Count;
    }

    private static void RedirectHandlersEndingAt(
        MethodBody targetBody,
        Instruction oldEnd,
        Instruction newEnd)
    {
        foreach (var handler in targetBody.ExceptionHandlers)
        {
            if (ReferenceEquals(handler.HandlerEnd, oldEnd))
            {
                handler.HandlerEnd = newEnd;
            }
        }
    }

    private static bool IsInTryRegion(MethodBody targetBody, int instructionIndex)
    {
        foreach (var handler in targetBody.ExceptionHandlers)
        {
            var tryStart = targetBody.Instructions.IndexOf(handler.TryStart);
            var tryEnd = targetBody.Instructions.IndexOf(handler.TryEnd);
            if (tryStart <= instructionIndex && instructionIndex < tryEnd)
            {
                return true;
            }
        }

        return false;
    }

    private static void ConvertInternalLeavesToBranches(MethodBody targetBody)
    {
        for (var i = 0; i < targetBody.Instructions.Count; i++)
        {
            var instruction = targetBody.Instructions[i];
            if (instruction.OpCode != OpCodes.Leave
                && instruction.OpCode != OpCodes.Leave_S
                || instruction.Operand is not Instruction target)
            {
                continue;
            }

            var targetIndex = targetBody.Instructions.IndexOf(target);
            if (targetIndex < 0 || !IsSameProtectedRegion(targetBody, i, targetIndex))
            {
                continue;
            }

            instruction.OpCode = OpCodes.Br;
        }
    }

    private static bool IsSameProtectedRegion(MethodBody targetBody, int instructionIndex, int targetIndex)
    {
        foreach (var handler in targetBody.ExceptionHandlers)
        {
            var tryStart = targetBody.Instructions.IndexOf(handler.TryStart);
            var tryEnd = targetBody.Instructions.IndexOf(handler.TryEnd);
            var instructionInTry = tryStart <= instructionIndex && instructionIndex < tryEnd;
            var targetInTry = tryStart <= targetIndex && targetIndex < tryEnd;
            if (instructionInTry != targetInTry)
            {
                return false;
            }

            var handlerStart = targetBody.Instructions.IndexOf(handler.HandlerStart);
            var handlerEnd = targetBody.Instructions.IndexOf(handler.HandlerEnd);
            var instructionInHandler = handlerStart <= instructionIndex && instructionIndex < handlerEnd;
            var targetInHandler = handlerStart <= targetIndex && targetIndex < handlerEnd;
            if (instructionInHandler != targetInHandler)
            {
                return false;
            }
        }

        return true;
    }

    private static void RedirectHandlersEndingAtEpilogue(
        MethodBody targetBody,
        Instruction epilogueEntry,
        Instruction suffixEntry)
    {
        var suffixIndex = targetBody.Instructions.IndexOf(suffixEntry);
        foreach (var handler in targetBody.ExceptionHandlers)
        {
            if (!ReferenceEquals(handler.HandlerEnd, epilogueEntry)
                || handler.HandlerStart == null
                || targetBody.Instructions.IndexOf(handler.HandlerStart) >= suffixIndex)
            {
                continue;
            }

            handler.HandlerEnd = suffixEntry;
        }
    }

    private static int FindCompilerCatchHandlerStart(MethodDefinition moveNext)
    {
        if (!moveNext.HasBody || moveNext.Body.ExceptionHandlers.Count == 0)
        {
            return moveNext.Body?.Instructions.Count ?? 0;
        }

        var catchHandler = FindCompilerCatchHandler(moveNext.Body.Instructions, moveNext.Body.ExceptionHandlers);
        if (catchHandler?.HandlerStart != null)
        {
            return moveNext.Body.Instructions.IndexOf(catchHandler.HandlerStart);
        }

        return moveNext.Body.ExceptionHandlers
            .Where(handler => handler.HandlerStart != null)
            .Select(handler => moveNext.Body.Instructions.IndexOf(handler.HandlerStart))
            .Where(index => index >= 0)
            .DefaultIfEmpty(moveNext.Body.Instructions.Count)
            .Min();
    }

    private static bool TryFindCompilerCatchTryStart(
        IList<Instruction> originalInstructions,
        IReadOnlyList<ExceptionHandler> originalHandlers,
        IReadOnlyDictionary<Instruction, Instruction> originalCloneMap,
        out Instruction protectedStart)
    {
        protectedStart = null!;
        var catchHandler = FindCompilerCatchHandler(originalInstructions, originalHandlers);
        if (catchHandler?.TryStart == null)
        {
            return false;
        }

        return originalCloneMap.TryGetValue(catchHandler.TryStart, out protectedStart!);
    }

    private static ExceptionHandler? FindCompilerCatchHandler(
        IList<Instruction> instructions,
        IEnumerable<ExceptionHandler> handlers)
    {
        return handlers
            .Where(handler => handler.HandlerType == ExceptionHandlerType.Catch
                && handler.TryStart != null
                && handler.TryEnd != null
                && handler.HandlerStart != null)
            .Select(handler => new
            {
                Handler = handler,
                TryStart = instructions.IndexOf(handler.TryStart),
                TryEnd = instructions.IndexOf(handler.TryEnd),
            })
            .Where(entry => entry.TryStart >= 0 && entry.TryEnd >= 0)
            .OrderBy(entry => entry.TryStart)
            .ThenByDescending(entry => entry.TryEnd)
            .Select(entry => entry.Handler)
            .FirstOrDefault();
    }

    private static Dictionary<Instruction, Instruction> BuildTemplateAnchorMap(
        List<Instruction> templateInstructions,
        int markerIndex,
        int awaitBlockEndIndex,
        Instruction targetWorkEntry,
        IReadOnlyDictionary<Instruction, Instruction> cloneMap)
    {
        var map = new Dictionary<Instruction, Instruction>(cloneMap);
        for (var i = markerIndex; i < awaitBlockEndIndex; i++)
        {
            map[templateInstructions[i]] = targetWorkEntry;
        }

        return map;
    }

    private static Instruction[] CloneInstructionRange(
        List<Instruction> source,
        int startInclusive,
        int endExclusive,
        MethodBody sourceBody,
        ModuleDefinition module,
        Dictionary<VariableDefinition, VariableDefinition> localMap,
        Dictionary<FieldDefinition, FieldDefinition> fieldMap,
        TypeDefinition templateStateMachine,
        TypeDefinition targetStateMachine,
        WeaveCaptureKind[] captureKinds,
        MethodDefinition captureMethod,
        AsyncIlTypeRewriter typeRewriter)
    {
        var count = Math.Max(0, endExclusive - startInclusive);
        var clones = new Instruction[count];
        var cloneMap = new Dictionary<Instruction, Instruction>();

        for (var i = 0; i < count; i++)
        {
            var sourceIndex = startInclusive + i;
            clones[i] = CloneWeaveInstruction(
                source[sourceIndex],
                sourceBody,
                module,
                localMap,
                fieldMap,
                templateStateMachine,
                targetStateMachine,
                captureKinds,
                captureMethod,
                typeRewriter);
            cloneMap[source[sourceIndex]] = clones[i];
        }

        IlRelocator.FixupBranchTargets(clones, cloneMap);
        return clones;
    }

    private static Instruction CloneWeaveInstruction(
        Instruction source,
        MethodBody sourceBody,
        ModuleDefinition module,
        Dictionary<VariableDefinition, VariableDefinition> localMap,
        Dictionary<FieldDefinition, FieldDefinition> fieldMap,
        TypeDefinition templateStateMachine,
        TypeDefinition targetStateMachine,
        WeaveCaptureKind[] captureKinds,
        MethodDefinition captureMethod,
        AsyncIlTypeRewriter typeRewriter)
    {
        var ilArgIndex = GetArgIlIndex(source);
        if (ilArgIndex >= 0 && captureKinds.Length > ilArgIndex)
        {
            var captureKind = captureKinds[ilArgIndex];
            if (captureKind != WeaveCaptureKind.None)
            {
                var captureLoad = WeaveCaptureInjector.CreateCaptureLoadInstruction(
                    captureKind,
                    captureMethod,
                    module);
                if (captureLoad != null)
                {
                    return captureLoad;
                }
            }
        }

        if (source.OpCode == OpCodes.Ldfld && source.Operand is FieldReference fieldReference)
        {
            if (TryRemapStateMachineField(
                    fieldReference,
                    templateStateMachine,
                    targetStateMachine,
                    fieldMap,
                    module,
                    out var mappedFieldRef))
            {
                return Instruction.Create(OpCodes.Ldfld, mappedFieldRef);
            }
        }

        if (source.OpCode == OpCodes.Stfld && source.Operand is FieldReference stFieldReference)
        {
            if (TryRemapStateMachineField(
                    stFieldReference,
                    templateStateMachine,
                    targetStateMachine,
                    fieldMap,
                    module,
                    out var mappedFieldRef))
            {
                return Instruction.Create(OpCodes.Stfld, mappedFieldRef);
            }
        }

        if (source.OpCode == OpCodes.Ldflda && source.Operand is FieldReference ldfldaField)
        {
            if (TryRemapStateMachineField(
                    ldfldaField,
                    templateStateMachine,
                    targetStateMachine,
                    fieldMap,
                    module,
                    out var mappedFieldRef))
            {
                return Instruction.Create(OpCodes.Ldflda, mappedFieldRef);
            }
        }

        if (TryRemapWeaveLocal(source, sourceBody, localMap, out var remappedLocal))
        {
            return typeRewriter.RewriteInstruction(remappedLocal);
        }

        if (source.Operand is MethodReference or FieldReference or TypeReference)
        {
            return typeRewriter.RewriteInstruction(source);
        }

        return source.Operand switch
        {
            null => Instruction.Create(source.OpCode),
            VariableDefinition variable => Instruction.Create(
                source.OpCode,
                localMap.TryGetValue(variable, out var mapped) ? mapped : variable),
            ParameterDefinition parameter => Instruction.Create(source.OpCode, parameter),
            string s => Instruction.Create(source.OpCode, s),
            int n => Instruction.Create(source.OpCode, n),
            long l => Instruction.Create(source.OpCode, l),
            float f => Instruction.Create(source.OpCode, f),
            double d => Instruction.Create(source.OpCode, d),
            byte b => Instruction.Create(source.OpCode, b),
            sbyte sb => Instruction.Create(source.OpCode, sb),
            Instruction target => Instruction.Create(source.OpCode, target),
            Instruction[] targets => Instruction.Create(source.OpCode, (Instruction[])targets.Clone()),
            _ => Instruction.Create(source.OpCode),
        };
    }

    private static bool TryRemapStateMachineField(
        FieldReference fieldReference,
        TypeDefinition templateStateMachine,
        TypeDefinition targetStateMachine,
        IReadOnlyDictionary<FieldDefinition, FieldDefinition> fieldMap,
        ModuleDefinition module,
        out FieldReference mappedFieldRef)
    {
        mappedFieldRef = null!;

        var resolved = fieldReference.Resolve();
        if (resolved != null && fieldMap.TryGetValue(resolved, out var mappedField))
        {
            mappedFieldRef = ImportStateMachineFieldReference(targetStateMachine, mappedField, module);
            return true;
        }

        if (fieldReference.DeclaringType.FullName != templateStateMachine.FullName
            && fieldReference.DeclaringType.Name != templateStateMachine.Name)
        {
            return false;
        }

        var targetField = targetStateMachine.Fields.FirstOrDefault(f => f.Name == fieldReference.Name);
        if (targetField == null)
        {
            return false;
        }

        mappedFieldRef = ImportStateMachineFieldReference(targetStateMachine, targetField, module);
        return true;
    }

    private static FieldReference ImportStateMachineFieldReference(
        TypeDefinition targetStateMachine,
        FieldDefinition field,
        ModuleDefinition module)
    {
        if (!targetStateMachine.HasGenericParameters)
        {
            return module.ImportReference(field);
        }

        var declaringType = new GenericInstanceType(module.ImportReference(targetStateMachine));
        foreach (var genericParameter in targetStateMachine.GenericParameters)
        {
            declaringType.GenericArguments.Add(genericParameter);
        }

        return new FieldReference(
            field.Name,
            module.ImportReference(field.FieldType),
            declaringType);
    }

    private static void RedirectSuffixExitsToEpilogue(Instruction[] suffixClones, Instruction epilogueEntry)
    {
        foreach (var clone in suffixClones)
        {
            if (clone.OpCode == OpCodes.Ret || clone.OpCode == OpCodes.Leave || clone.OpCode == OpCodes.Leave_S)
            {
                clone.OpCode = OpCodes.Br;
                clone.Operand = epilogueEntry;
            }
        }
    }

    private static Instruction CloneOriginalInstruction(Instruction source)
    {
        if (source.Operand == null)
        {
            return Instruction.Create(source.OpCode);
        }

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
            _ => throw new NotSupportedException($"AsyncWeaveSplicer：不支持的原始指令操作数类型 '{source.Operand.GetType().FullName}'。"),
        };
    }

    private static void AppendInstruction(ILProcessor il, Instruction? instruction)
    {
        if (instruction is null)
        {
            throw new InvalidOperationException("AsyncWeaveSplicer：尝试向 MoveNext 追加 null 指令。");
        }

        il.Append(instruction);
    }

    private static void AppendOriginalRange(
        ILProcessor il,
        Instruction[] origClones,
        int startInclusive,
        int endExclusive,
        string segmentName)
    {
        for (var i = startInclusive; i < endExclusive; i++)
        {
            if (i < 0 || i >= origClones.Length)
            {
                throw new InvalidOperationException(
                    $"AsyncWeaveSplicer：{segmentName} 索引 {i} 超出范围（长度 {origClones.Length}）。");
            }

            AppendInstruction(il, origClones[i]);
        }
    }

    private static void AppendCloneRange(ILProcessor il, Instruction[] clones, string segmentName)
    {
        for (var i = 0; i < clones.Length; i++)
        {
            if (clones[i] is null)
            {
                throw new InvalidOperationException(
                    $"AsyncWeaveSplicer：{segmentName} 在索引 {i} 处为 null。");
            }

            AppendInstruction(il, clones[i]);
        }
    }

    private static void RetargetBranchOperandIfMatches(
        Instruction instruction,
        Instruction matchTarget,
        Instruction newTarget)
    {
        if (instruction.Operand is Instruction branchTarget && ReferenceEquals(branchTarget, matchTarget))
        {
            instruction.Operand = newTarget;
            return;
        }

        if (instruction.Operand is Instruction[] switchTargets)
        {
            for (var i = 0; i < switchTargets.Length; i++)
            {
                if (ReferenceEquals(switchTargets[i], matchTarget))
                {
                    switchTargets[i] = newTarget;
                }
            }
        }
    }

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
        return sourceOpCode.Code switch
        {
            Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3
                or Code.Ldloc_S or Code.Ldloc => Instruction.Create(OpCodes.Ldloc, mappedLocal),
            Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3
                or Code.Stloc_S or Code.Stloc => Instruction.Create(OpCodes.Stloc, mappedLocal),
            Code.Ldloca_S or Code.Ldloca => Instruction.Create(OpCodes.Ldloca, mappedLocal),
            _ => Instruction.Create(sourceOpCode, mappedLocal),
        };
    }

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
}
