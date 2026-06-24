using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace SharpWeaver;

/// <summary>
/// After weave splicing, fixes up branch targets and exception handler boundary references.
/// </summary>
/// <remarks>
/// <para>
/// When weave splicing (<see cref="WeaveSplicer.TrySplice"/>) merges two IL segments into one method body,
/// branch targets and exception handler <c>TryStart</c> / <c>TryEnd</c> / <c>HandlerStart</c> / <c>HandlerEnd</c>
/// still point to original instruction objects. <see cref="IlRelocator"/> provides utility methods to fix them up
/// to cloned instructions via an old→new mapping dictionary.
/// </para>
/// </remarks>
public static class IlRelocator
{
    /// <summary>
    /// Fixes up branch targets for all instructions in <paramref name="instructions"/>
    /// (single targets and <c>switch</c> multi-targets): if a branch target exists in <paramref name="map"/>,
    /// it is replaced with the mapped new instruction.
    /// </summary>
    /// <param name="instructions">Instruction sequence to fix up.</param>
    /// <param name="map">Old instruction → new instruction mapping dictionary.</param>
    public static void FixupBranchTargets(
        IList<Instruction> instructions,
        IReadOnlyDictionary<Instruction, Instruction> map)
    {
        foreach (var instr in instructions)
        {
            if (instr.Operand is Instruction branchTarget
                && map.TryGetValue(branchTarget, out var newTarget))
            {
                instr.Operand = newTarget;
            }
            else if (instr.Operand is Instruction[] switchTargets)
            {
                for (var j = 0; j < switchTargets.Length; j++)
                {
                    if (map.TryGetValue(switchTargets[j], out var newSwitchTarget))
                    {
                        switchTargets[j] = newSwitchTarget;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Expands short branch instructions to long branch instructions to prevent jump distance overflow after IL splicing.
    /// </summary>
    /// <param name="instructions">Instruction sequence to normalize.</param>
    public static void ExpandShortBranches(IList<Instruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            instruction.OpCode = instruction.OpCode.Code switch
            {
                Code.Br_S => OpCodes.Br,
                Code.Brfalse_S => OpCodes.Brfalse,
                Code.Brtrue_S => OpCodes.Brtrue,
                Code.Beq_S => OpCodes.Beq,
                Code.Bge_S => OpCodes.Bge,
                Code.Bgt_S => OpCodes.Bgt,
                Code.Ble_S => OpCodes.Ble,
                Code.Blt_S => OpCodes.Blt,
                Code.Bne_Un_S => OpCodes.Bne_Un,
                Code.Bge_Un_S => OpCodes.Bge_Un,
                Code.Bgt_Un_S => OpCodes.Bgt_Un,
                Code.Ble_Un_S => OpCodes.Ble_Un,
                Code.Blt_Un_S => OpCodes.Blt_Un,
                Code.Leave_S => OpCodes.Leave,
                _ => instruction.OpCode,
            };
        }
    }

    /// <summary>
    /// Clones each exception handler from <paramref name="sourceHandlers"/> into
    /// <paramref name="targetHandlers"/>, mapping handler boundary instructions through <paramref name="map"/>;
    /// when a mapping is missing, the original instruction is preserved.
    /// </summary>
    /// <param name="targetHandlers">Target exception handler collection (written to).</param>
    /// <param name="sourceHandlers">Source exception handler collection.</param>
    /// <param name="map">Old instruction → new instruction mapping dictionary.</param>
    /// <param name="module">Module used to import <c>CatchType</c> type references.</param>
    public static void RelocateExceptionHandlers(
        Collection<ExceptionHandler> targetHandlers,
        IEnumerable<ExceptionHandler> sourceHandlers,
        IReadOnlyDictionary<Instruction, Instruction> map,
        ModuleDefinition module)
    {
        foreach (var handler in sourceHandlers)
        {
            var newHandler = new ExceptionHandler(handler.HandlerType)
            {
                CatchType = handler.CatchType != null
                    ? module.ImportReference(handler.CatchType)
                    : null,
                TryStart = RemapOrKeep(handler.TryStart, map),
                TryEnd = RemapOrKeep(handler.TryEnd, map),
                HandlerStart = RemapOrKeep(handler.HandlerStart, map),
                HandlerEnd = handler.HandlerEnd != null
                    ? RemapOrKeep(handler.HandlerEnd, map)
                    : null,
                FilterStart = handler.FilterStart != null
                    ? RemapOrKeep(handler.FilterStart, map)
                    : null,
            };
            targetHandlers.Add(newHandler);
        }
    }

    private static Instruction RemapOrKeep(
        Instruction instr,
        IReadOnlyDictionary<Instruction, Instruction> map)
        => map.TryGetValue(instr, out var mapped) ? mapped : instr;
}
