using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver;

/// <summary>Locates the <c>OriginalBodyAsync</c> marker and await resume points in an async state machine <c>MoveNext</c>.</summary>
public static class AsyncAwaitMarkerLocator
{
    private const string StateFieldName = "<>1__state";

    /// <summary>Async marker bounds.</summary>
    /// <param name="MarkerCallIndex">Index of the marker <c>call</c> instruction.</param>
    /// <param name="AwaitBlockEndIndex">Index of the first instruction after the await block (resume point).</param>
    /// <param name="InitialStateWorkStartIndex">Index of the first instruction in the initial state (-1) working area.</param>
    public readonly record struct AsyncMarkerBounds(
        int MarkerCallIndex,
        int AwaitBlockEndIndex,
        int InitialStateWorkStartIndex);

    /// <summary>
    /// Locates the <c>await WeaveTemplate.OriginalBodyAsync()</c> bounds in the weave template <c>MoveNext</c>.
    /// </summary>
    public static bool TryLocate(
        MethodDefinition templateMoveNext,
        out AsyncMarkerBounds bounds,
        out string? error)
    {
        bounds = default;
        error = null;

        if (!templateMoveNext.HasBody)
        {
            error = "Weave template MoveNext has no method body.";
            return false;
        }

        var instructions = templateMoveNext.Body.Instructions;
        var markerIndex = FindMarkerCallIndex(instructions);
        if (markerIndex < 0)
        {
            error = "WeaveTemplate.OriginalBodyAsync() marker call not found in weave template MoveNext.";
            return false;
        }

        if (!TryFindAwaitResumeIndex(instructions, markerIndex, out var resumeIndex))
        {
            error = "Await resume point for OriginalBodyAsync not found in weave template MoveNext.";
            return false;
        }

        var workStart = FindInitialStateWorkStartIndex(instructions);
        bounds = new AsyncMarkerBounds(markerIndex, resumeIndex, workStart);
        return true;
    }

    /// <summary>Locates the initial working area entry in the state machine (fall-through path of state dispatch <c>switch</c> or <c>brfalse</c>).</summary>
    /// <param name="instructions"><c>MoveNext</c> instruction list.</param>
    /// <returns>Index of the first instruction in the initial working area.</returns>
    public static int FindInitialStateWorkStartIndex(IList<Instruction> instructions)
    {
        for (var i = 0; i < instructions.Count - 1; i++)
        {
            if (!IsStateDispatchSwitch(instructions, i))
            {
                continue;
            }

            return FollowDispatchFallThroughBranches(instructions, i + 1);
        }

        for (var i = 0; i < instructions.Count - 1; i++)
        {
            if (!IsStateDispatchBranch(instructions, i))
            {
                continue;
            }

            return FollowDispatchFallThroughBranches(instructions, i + 1);
        }

        return 0;
    }

    private static int FollowDispatchFallThroughBranches(IList<Instruction> instructions, int startIndex)
    {
        var index = startIndex;
        while (index < instructions.Count && IsDispatchFallThroughBranch(instructions[index]))
        {
            if (instructions[index].Operand is not Instruction branchTarget)
            {
                break;
            }

            var targetIndex = instructions.IndexOf(branchTarget);
            if (targetIndex < 0 || targetIndex == index)
            {
                break;
            }

            if (TryFindStateComparisonConditionalBranch(instructions, targetIndex, out var conditionalBranchIndex))
            {
                index = conditionalBranchIndex + 1;
                continue;
            }

            index = targetIndex;
        }

        return index;
    }

    private static bool IsDispatchFallThroughBranch(Instruction instruction) =>
        instruction.OpCode == OpCodes.Br || instruction.OpCode == OpCodes.Br_S;

    private static bool TryFindStateComparisonConditionalBranch(
        IList<Instruction> instructions,
        int startIndex,
        out int conditionalBranchIndex)
    {
        conditionalBranchIndex = -1;
        var sawStateLoad = false;
        for (var i = startIndex; i < Math.Min(startIndex + 6, instructions.Count); i++)
        {
            var opCode = instructions[i].OpCode;
            if (opCode == OpCodes.Br || opCode == OpCodes.Br_S)
            {
                return false;
            }

            if (IsStateLocalLoad(instructions[i]))
            {
                sawStateLoad = true;
                continue;
            }

            if (sawStateLoad && IsStateDispatchConditionalBranch(opCode))
            {
                conditionalBranchIndex = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>Counts the number of <c>OriginalBodyAsync</c> marker calls in the weave template <c>MoveNext</c>.</summary>
    public static int CountMarkerCalls(MethodDefinition method)
    {
        if (!method.HasBody)
        {
            return 0;
        }

        var count = 0;
        foreach (var instruction in method.Body.Instructions)
        {
            if (IsMarkerCall(instruction))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Locates the final successful completion points (<c>ret</c> after <c>SetResult</c>) in the target <c>MoveNext</c>.</summary>
    public static IReadOnlyList<Instruction> FindFinalCompletionReturns(MethodDefinition targetMoveNext)
    {
        var results = new List<Instruction>();
        if (!targetMoveNext.HasBody)
        {
            return results;
        }

        var instructions = targetMoveNext.Body.Instructions;
        var stateField = FindStateField(targetMoveNext.DeclaringType);
        if (stateField == null)
        {
            return results;
        }

        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (instruction.OpCode != OpCodes.Ret)
            {
                continue;
            }

            if (IsPrecededByBuilderSetResult(instructions, i)
                && IsPrecededByTerminalStateStore(instructions, i, stateField))
            {
                results.Add(instruction);
            }
        }

        return results;
    }

    /// <summary>Locates the start index of the state machine completion epilogue (<c>state = -2</c>, <c>SetResult</c>, <c>ret</c>).</summary>
    public static int FindCompletionEpilogueStart(MethodDefinition moveNext)
    {
        if (!moveNext.HasBody)
        {
            return 0;
        }

        var instructions = moveNext.Body.Instructions;
        var stateField = FindStateField(moveNext.DeclaringType);
        if (stateField == null)
        {
            return instructions.Count;
        }

        for (var i = 0; i < instructions.Count; i++)
        {
            if (!IsTerminalStateStoreAt(instructions, i, stateField))
            {
                continue;
            }

            if (!HasBuilderSetResultBeforeControlExit(instructions, i))
            {
                continue;
            }

            for (var j = i - 1; j >= Math.Max(0, i - 3); j--)
            {
                if (instructions[j].OpCode == OpCodes.Ldarg_0)
                {
                    return j;
                }
            }

            return Math.Max(0, i - 2);
        }

        return instructions.Count;
    }

    /// <summary>Locates the start index of the weave template user postfix (skipping await resume boilerplate code).</summary>
    public static int FindTemplateUserSuffixStart(
        MethodDefinition templateMoveNext,
        int markerIndex,
        int awaitBlockEndIndex)
    {
        if (!templateMoveNext.HasBody)
        {
            return awaitBlockEndIndex;
        }

        var instructions = templateMoveNext.Body.Instructions;
        var suffixStart = awaitBlockEndIndex;
        for (var i = awaitBlockEndIndex; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                && instruction.Operand is MethodReference methodReference
                && methodReference.Name == "GetResult")
            {
                suffixStart = i + 1;
                break;
            }
        }

        while (suffixStart < instructions.Count && instructions[suffixStart].OpCode == OpCodes.Nop)
        {
            suffixStart++;
        }

        return TryFindReachableUserSuffixStart(instructions, suffixStart, out var reachableSuffixStart)
            ? reachableSuffixStart
            : suffixStart;
    }

    /// <summary>Locates the end index of the weave template user postfix (excluding the <c>leave</c> exiting the user postfix and the state machine epilogue).</summary>
    public static int FindTemplateUserSuffixEnd(MethodDefinition templateMoveNext, int suffixStartIndex)
    {
        if (!templateMoveNext.HasBody)
        {
            return suffixStartIndex;
        }

        var instructions = templateMoveNext.Body.Instructions;
        for (var i = suffixStartIndex; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (instruction.OpCode == OpCodes.Leave
                || instruction.OpCode == OpCodes.Leave_S
                || instruction.OpCode == OpCodes.Ret)
            {
                return i;
            }

            if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                && instruction.Operand is MethodReference methodReference
                && (methodReference.Name == "SetResult" || methodReference.Name == "SetException"))
            {
                return i;
            }
        }

        return suffixStartIndex;
    }

    private static int FindMarkerCallIndex(IList<Instruction> instructions)
    {
        for (var i = 0; i < instructions.Count; i++)
        {
            if (IsMarkerCall(instructions[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryFindReachableUserSuffixStart(
        IList<Instruction> instructions,
        int suffixStart,
        out int reachableSuffixStart)
    {
        reachableSuffixStart = suffixStart;
        var scanIndex = suffixStart;
        var visited = new HashSet<int>();

        while (scanIndex < instructions.Count && visited.Add(scanIndex))
        {
            var instruction = instructions[scanIndex];
            if (IsAwaiterPlumbing(instruction))
            {
                scanIndex++;
                continue;
            }

            if (instruction.OpCode == OpCodes.Ret || IsBuilderCompletionCall(instruction))
            {
                return false;
            }

            if ((instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S)
                && instruction.Operand is Instruction leaveTarget)
            {
                var leaveTargetIndex = instructions.IndexOf(leaveTarget);
                if (leaveTargetIndex > scanIndex)
                {
                    reachableSuffixStart = leaveTargetIndex;
                    scanIndex = leaveTargetIndex;
                    continue;
                }

                return false;
            }

            if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
            {
                return true;
            }

            scanIndex++;
        }

        return false;
    }

    private static bool IsMarkerCall(Instruction instruction) =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is MethodReference methodReference
        && methodReference.Name == SharpWeaverMetadata.OriginalBodyAsyncMethod
        && methodReference.DeclaringType.FullName == SharpWeaverMetadata.WeaveTemplate;

    private static bool IsBuilderCompletionCall(Instruction instruction) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
        && instruction.Operand is MethodReference methodReference
        && methodReference.Name is "SetResult" or "SetException";

    private static bool TryFindAwaitResumeIndex(IList<Instruction> instructions, int markerIndex, out int resumeIndex)
    {
        resumeIndex = -1;
        for (var i = markerIndex + 1; i < instructions.Count; i++)
        {
            if (instructions[i].Operand is not MethodReference methodReference)
            {
                continue;
            }

            if (methodReference.Name != "get_IsCompleted")
            {
                continue;
            }

            for (var j = i + 1; j < Math.Min(i + 4, instructions.Count); j++)
            {
                var branch = instructions[j];
                if (branch.OpCode != OpCodes.Brtrue && branch.OpCode != OpCodes.Brtrue_S)
                {
                    continue;
                }

                if (branch.Operand is Instruction target)
                {
                    resumeIndex = instructions.IndexOf(target);
                    return resumeIndex >= 0;
                }
            }
        }

        return false;
    }

    private static bool IsStateDispatchBranch(IList<Instruction> instructions, int branchIndex)
    {
        var op = instructions[branchIndex].OpCode;
        if (op != OpCodes.Brfalse
            && op != OpCodes.Brfalse_S
            && op != OpCodes.Brtrue
            && op != OpCodes.Brtrue_S)
        {
            return false;
        }

        for (var j = Math.Max(0, branchIndex - 4); j < branchIndex; j++)
        {
            if (IsStateLocalLoad(instructions[j]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStateDispatchSwitch(IList<Instruction> instructions, int switchIndex)
    {
        if (instructions[switchIndex].OpCode != OpCodes.Switch)
        {
            return false;
        }

        for (var j = Math.Max(0, switchIndex - 4); j < switchIndex; j++)
        {
            if (IsStateLocalLoad(instructions[j]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStateLocalLoad(Instruction instruction)
    {
        if (instruction.OpCode == OpCodes.Ldloc_0)
        {
            return true;
        }

        if ((instruction.OpCode == OpCodes.Ldloc || instruction.OpCode == OpCodes.Ldloc_S)
            && instruction.Operand is VariableDefinition variable)
        {
            return variable.Index == 0;
        }

        return false;
    }

    private static bool IsStateDispatchConditionalBranch(OpCode opCode) =>
        opCode == OpCodes.Brfalse
        || opCode == OpCodes.Brfalse_S
        || opCode == OpCodes.Brtrue
        || opCode == OpCodes.Brtrue_S
        || opCode == OpCodes.Beq
        || opCode == OpCodes.Beq_S
        || opCode == OpCodes.Bne_Un
        || opCode == OpCodes.Bne_Un_S;

    private static FieldDefinition? FindStateField(TypeDefinition stateMachineType)
    {
        foreach (var field in stateMachineType.Fields)
        {
            if (field.Name == StateFieldName)
            {
                return field;
            }
        }

        return null;
    }

    private static bool IsPrecededByBuilderSetResult(IList<Instruction> instructions, int retIndex)
    {
        for (var i = retIndex - 1; i >= Math.Max(0, retIndex - 8); i--)
        {
            if (instructions[i].OpCode != OpCodes.Call)
            {
                continue;
            }

            if (instructions[i].Operand is MethodReference methodReference
                && methodReference.Name == "SetResult")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAwaiterPlumbing(Instruction instruction)
    {
        if (instruction.OpCode == OpCodes.Ldarg_0
            || instruction.OpCode == OpCodes.Ldarg
            || instruction.OpCode == OpCodes.Ldarg_S
            || instruction.OpCode == OpCodes.Dup
            || instruction.OpCode == OpCodes.Pop
            || instruction.OpCode == OpCodes.Initobj
            || instruction.OpCode == OpCodes.Stloc
            || instruction.OpCode == OpCodes.Stloc_S
            || instruction.OpCode == OpCodes.Stloc_0
            || instruction.OpCode == OpCodes.Stloc_1
            || instruction.OpCode == OpCodes.Stloc_2
            || instruction.OpCode == OpCodes.Stloc_3
            || instruction.OpCode == OpCodes.Ldloc
            || instruction.OpCode == OpCodes.Ldloc_S
            || instruction.OpCode == OpCodes.Ldloc_0
            || instruction.OpCode == OpCodes.Ldloc_1
            || instruction.OpCode == OpCodes.Ldloc_2
            || instruction.OpCode == OpCodes.Ldloc_3
            || instruction.OpCode == OpCodes.Ldloca
            || instruction.OpCode == OpCodes.Ldloca_S
            || instruction.OpCode == OpCodes.Nop)
        {
            return true;
        }

        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
        {
            return false;
        }

        if (instruction.Operand is not MethodReference methodReference)
        {
            return false;
        }

        return methodReference.Name is "GetResult" or "get_IsCompleted" or "GetAwaiter"
            or "AwaitUnsafeOnCompleted" or "AwaitOnCompleted";
    }

    private static bool IsPrecededByTerminalStateStore(
        IList<Instruction> instructions,
        int retIndex,
        FieldDefinition stateField)
    {
        for (var i = retIndex - 1; i >= 0; i--)
        {
            if (instructions[i].OpCode == OpCodes.Leave
                || instructions[i].OpCode == OpCodes.Leave_S
                || instructions[i].OpCode == OpCodes.Ret)
            {
                return false;
            }

            if (IsTerminalStateStoreAt(instructions, i, stateField))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTerminalStateStoreAt(
        IList<Instruction> instructions,
        int storeIndex,
        FieldDefinition stateField)
    {
        var instruction = instructions[storeIndex];
        if (instruction.OpCode != OpCodes.Stfld)
        {
            return false;
        }

        if (instruction.Operand is not FieldReference fieldReference
            || fieldReference.Name != stateField.Name)
        {
            return false;
        }

        for (var j = storeIndex - 1; j >= Math.Max(0, storeIndex - 3); j--)
        {
            if (instructions[j].OpCode == OpCodes.Ldc_I4_S
                && instructions[j].Operand is sbyte sbyteValue
                && sbyteValue == -2)
            {
                return true;
            }

            if (instructions[j].OpCode == OpCodes.Ldc_I4
                && instructions[j].Operand is int value
                && value == -2)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBuilderSetResultBeforeControlExit(
        IList<Instruction> instructions,
        int fromIndex)
    {
        for (var i = fromIndex + 1; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode == OpCodes.Leave
                || instructions[i].OpCode == OpCodes.Leave_S
                || instructions[i].OpCode == OpCodes.Ret)
            {
                return false;
            }

            if (instructions[i].OpCode != OpCodes.Call)
            {
                continue;
            }

            if (instructions[i].Operand is MethodReference methodReference
                && methodReference.Name == "SetException")
            {
                return false;
            }

            if (instructions[i].Operand is MethodReference resultMethodReference
                && resultMethodReference.Name == "SetResult")
            {
                return true;
            }
        }

        return false;
    }
}
