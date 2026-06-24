using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver.Tests;

/// <summary>Utility for inspecting the IL integrity of woven async <c>MoveNext</c> methods.</summary>
internal static class WovenMoveNextInspector
{
    /// <summary>Collects IL integrity issues for async weave targets in the specified assembly.</summary>
    /// <param name="assemblyPath">Path to the woven assembly.</param>
    /// <param name="typeNameFilter">Only inspect targets whose type full name contains this substring; pass null to inspect all.</param>
    /// <returns>List of issue descriptions; an empty list indicates no issues found.</returns>
    public static List<string> CollectIssues(string assemblyPath, string? typeNameFilter = null)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(
            assemblyPath,
            new ReaderParameters { ReadingMode = ReadingMode.Immediate });

        var issues = new List<string>();
        foreach (var type in assembly.MainModule.Types)
        {
            foreach (var method in type.Methods)
            {
                if (!SharpWeaver.AsyncMethodHelper.IsCompilerAsyncMethod(method))
                {
                    continue;
                }

                if (typeNameFilter is not null
                    && !type.FullName.Contains(typeNameFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!SharpWeaver.AsyncMethodHelper.TryResolveMoveNext(method, out var moveNext, out var stateMachine))
                {
                    continue;
                }

                SharpWeaver.AsyncMethodHelper.StripAsyncMethodBodyDebugInfo(moveNext);

                var label = $"{type.FullName}.{method.Name}";

                var duplicateFieldNames = stateMachine.Fields
                    .GroupBy(field => field.Name)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToList();
                if (duplicateFieldNames.Count > 0)
                {
                    issues.Add($"{label}: duplicate fields [{string.Join(", ", duplicateFieldNames)}]");
                }

                if (!moveNext.HasBody)
                {
                    issues.Add($"{label}: MoveNext has no body.");
                    continue;
                }

                var body = moveNext.Body;
                var instructionSet = new HashSet<Instruction>(body.Instructions);
                if (body.Instructions.Count == 0 || body.Instructions[^1].OpCode != OpCodes.Ret)
                {
                    issues.Add($"{label}: MoveNext terminal instruction is not ret.");
                }

                for (var i = 0; i < body.Instructions.Count; i++)
                {
                    var instruction = body.Instructions[i];
                    if (instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S)
                    {
                        if (instruction.Operand is not Instruction leaveTarget)
                        {
                            issues.Add($"{label}: leave at IL_{instruction.Offset:X4} has invalid operand.");
                        }
                        else if (!instructionSet.Contains(leaveTarget))
                        {
                            issues.Add($"{label}: leave at IL_{instruction.Offset:X4} targets instruction outside body.");
                        }
                    }

                    if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
                    {
                        if (instruction.Operand is null)
                        {
                            issues.Add($"{label}: branch {instruction.OpCode} at IL_{instruction.Offset:X4} has null operand.");
                        }
                        else if (instruction.Operand is Instruction branchTarget && !instructionSet.Contains(branchTarget))
                        {
                            issues.Add($"{label}: branch {instruction.OpCode} at IL_{instruction.Offset:X4} targets instruction outside body.");
                        }
                    }

                    if (instruction.OpCode == OpCodes.Switch && instruction.Operand is Instruction[] switchTargets)
                    {
                        for (var targetIndex = 0; targetIndex < switchTargets.Length; targetIndex++)
                        {
                            if (!instructionSet.Contains(switchTargets[targetIndex]))
                            {
                                issues.Add($"{label}: switch at IL_{instruction.Offset:X4} target[{targetIndex}] is outside body.");
                            }
                        }
                    }
                }

                foreach (var handler in body.ExceptionHandlers)
                {
                    if (handler.HandlerStart is null || handler.HandlerEnd is null)
                    {
                        issues.Add($"{label}: exception handler with null HandlerStart/HandlerEnd.");
                        continue;
                    }

                    if (!instructionSet.Contains(handler.HandlerStart)
                        || !instructionSet.Contains(handler.HandlerEnd)
                        || (handler.TryStart is not null && !instructionSet.Contains(handler.TryStart))
                        || (handler.TryEnd is not null && !instructionSet.Contains(handler.TryEnd)))
                    {
                        issues.Add($"{label}: exception handler references instruction outside body.");
                    }

                    if (handler.TryStart is not null
                        && handler.TryEnd is not null
                        && body.Instructions.IndexOf(handler.TryStart) >= body.Instructions.IndexOf(handler.TryEnd))
                    {
                        issues.Add($"{label}: exception handler TryStart is not before TryEnd.");
                    }

                    if (body.Instructions.IndexOf(handler.HandlerStart) >= body.Instructions.IndexOf(handler.HandlerEnd))
                    {
                        issues.Add($"{label}: exception handler HandlerStart is not before HandlerEnd.");
                    }
                }

                var taskAwaiterFields = stateMachine.Fields
                    .Where(field => field.Name.StartsWith("<>u__", StringComparison.Ordinal)
                        && field.FieldType.FullName == "System.Runtime.CompilerServices.TaskAwaiter")
                    .Select(field => field.Name)
                    .ToList();
                if (taskAwaiterFields.Count > 0
                    && method.ReturnType.FullName?.Contains("GDTask", StringComparison.Ordinal) == true)
                {
                    issues.Add($"{label}: GDTask state machine has TaskAwaiter fields [{string.Join(", ", taskAwaiterFields)}].");
                }
            }
        }

        return issues;
    }

    /// <summary>Checks whether GDTask MoveNext still references BCL Task weave artifact types.</summary>
    /// <param name="assemblyPath">Path to the woven assembly.</param>
    /// <param name="methodNameFilter">Only inspect targets whose type full name or method name contains this substring.</param>
    public static List<string> CollectTaskArtifactReferences(string assemblyPath, string? methodNameFilter = null)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(
            assemblyPath,
            new ReaderParameters { ReadingMode = ReadingMode.Immediate });

        var issues = new List<string>();
        foreach (var type in assembly.MainModule.Types)
        {
            foreach (var method in type.Methods)
            {
                if (!SharpWeaver.AsyncMethodHelper.IsCompilerAsyncMethod(method))
                {
                    continue;
                }

                if (methodNameFilter is not null
                    && !type.FullName.Contains(methodNameFilter, StringComparison.Ordinal)
                    && !method.Name.Contains(methodNameFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!method.ReturnType.FullName?.Contains("GDTask", StringComparison.Ordinal) ?? true)
                {
                    continue;
                }

                if (!SharpWeaver.AsyncMethodHelper.TryResolveMoveNext(method, out var moveNext, out _))
                {
                    continue;
                }

                SharpWeaver.AsyncMethodHelper.StripAsyncMethodBodyDebugInfo(moveNext);

                var label = $"{type.FullName}.{method.Name}";
                foreach (var instruction in moveNext.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case MethodReference methodReference
                            when methodReference.DeclaringType.FullName.StartsWith(
                                "System.Runtime.CompilerServices.AsyncTaskMethodBuilder", StringComparison.Ordinal)
                            || methodReference.DeclaringType.FullName == "System.Threading.Tasks.Task":
                            issues.Add($"{label}: calls {methodReference.DeclaringType.FullName}::{methodReference.Name}");
                            break;
                        case FieldReference fieldReference
                            when fieldReference.FieldType.FullName.StartsWith(
                                "System.Runtime.CompilerServices.AsyncTaskMethodBuilder", StringComparison.Ordinal)
                            || fieldReference.FieldType.FullName.StartsWith(
                                "System.Runtime.CompilerServices.TaskAwaiter", StringComparison.Ordinal):
                            issues.Add($"{label}: uses field type {fieldReference.FieldType.FullName} ({fieldReference.Name})");
                            break;
                    }
                }
            }
        }

        return issues;
    }

    /// <summary>Checks whether the woven <c>MoveNext</c> of a specified async method calls a given method name.</summary>
    public static bool MoveNextCallsMethod(
        string assemblyPath,
        string typeNameSubstring,
        string outerMethodName,
        string calleeMethodName)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(
            assemblyPath,
            new ReaderParameters { ReadingMode = ReadingMode.Immediate });

        foreach (var type in assembly.MainModule.Types)
        {
            if (!type.FullName.Contains(typeNameSubstring, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var method in type.Methods)
            {
                if (method.Name != outerMethodName)
                {
                    continue;
                }

                if (!SharpWeaver.AsyncMethodHelper.TryResolveMoveNext(method, out var moveNext, out _))
                {
                    return false;
                }

                SharpWeaver.AsyncMethodHelper.StripAsyncMethodBodyDebugInfo(moveNext);

                return moveNext.Body.Instructions.Any(instruction =>
                    (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                    && instruction.Operand is MethodReference methodReference
                    && methodReference.Name == calleeMethodName);
            }
        }

        return false;
    }
}
