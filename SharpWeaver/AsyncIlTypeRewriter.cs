using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver;

/// <summary>Rewrites BCL <c>Task</c> / <c>AsyncTaskMethodBuilder</c> types in the weave template to the target async type.</summary>
public sealed class AsyncIlTypeRewriter
{
    private const string TaskFullName = "System.Threading.Tasks.Task";
    private const string TaskBuilderFullName = "System.Runtime.CompilerServices.AsyncTaskMethodBuilder";
    private const string TaskBuilderGenericFullName = "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1";
    private const string StateFieldName = "<>1__state";
    private const string BuilderFieldName = "<>t__builder";

    private readonly ModuleDefinition _module;
    private readonly TypeReference _targetReturnType;
    private readonly TypeReference? _targetResultType;
    private readonly TypeReference _sourceBuilderType;
    private readonly TypeReference _targetBuilderType;

    private AsyncIlTypeRewriter(
        ModuleDefinition module,
        TypeReference targetReturnType,
        TypeReference? targetResultType,
        TypeReference sourceBuilderType,
        TypeReference targetBuilderType)
    {
        _module = module;
        _targetReturnType = targetReturnType;
        _targetResultType = targetResultType;
        _sourceBuilderType = sourceBuilderType;
        _targetBuilderType = targetBuilderType;
    }

    /// <summary>Creates a type rewriter from the target outer async method and template state machine.</summary>
    public static bool TryCreate(
        MethodDefinition outerMethod,
        TypeDefinition templateStateMachine,
        TypeDefinition targetStateMachine,
        ModuleDefinition module,
        out AsyncIlTypeRewriter rewriter,
        out string? error)
    {
        rewriter = null!;
        error = null;

        if (!TryResolveBuilderType(templateStateMachine, out var sourceBuilder, out error))
        {
            return false;
        }

        if (!TryResolveBuilderType(targetStateMachine, out var targetBuilder, out error))
        {
            return false;
        }

        var targetReturn = module.ImportReference(outerMethod.ReturnType);
        TypeReference? targetResult = null;
        if (outerMethod.ReturnType is GenericInstanceType genericReturn)
        {
            targetResult = module.ImportReference(genericReturn.GenericArguments[0]);
        }

        rewriter = new AsyncIlTypeRewriter(
            module,
            targetReturn,
            targetResult,
            module.ImportReference(sourceBuilder),
            module.ImportReference(targetBuilder));
        return true;
    }

    /// <summary>Rewrites the type reference operand of a single instruction.</summary>
    public Instruction RewriteInstruction(Instruction source)
    {
        if (source.Operand == null)
        {
            return Instruction.Create(source.OpCode);
        }

        return source.Operand switch
        {
            MethodReference methodReference => RewriteMethodReference(source, methodReference),
            FieldReference fieldReference => RewriteFieldReference(source, fieldReference),
            TypeReference typeReference => Instruction.Create(
                source.OpCode,
                RewriteTypeReference(typeReference)),
            ParameterDefinition parameter => Instruction.Create(source.OpCode, parameter),
            VariableDefinition variable => Instruction.Create(source.OpCode, variable),
            string s => Instruction.Create(source.OpCode, s),
            int n => Instruction.Create(source.OpCode, n),
            Instruction target => Instruction.Create(source.OpCode, target),
            _ => throw new NotSupportedException(
                $"AsyncIlTypeRewriter: unsupported instruction operand type '{source.Operand.GetType().FullName}'."),
        };
    }

    /// <summary>Rewrites a type reference.</summary>
    public TypeReference RewriteTypeReference(TypeReference typeReference)
    {
        var imported = _module.ImportReference(typeReference);
        if (IsTaskType(imported))
        {
            return _targetReturnType;
        }

        if (IsSourceBuilderType(imported))
        {
            return _targetBuilderType;
        }

        if (imported is GenericInstanceType generic)
        {
            var element = generic.ElementType;
            if (element.FullName == TaskBuilderGenericFullName)
            {
                return _targetBuilderType;
            }

            if (element.FullName == TaskFullName && _targetResultType != null)
            {
                return _module.ImportReference(
                    _module.ImportReference(_targetReturnType).Resolve() is { } resolved
                        && resolved.HasGenericParameters
                        ? new GenericInstanceType(resolved) { GenericArguments = { _targetResultType } }
                        : _targetReturnType);
            }
        }

        return imported;
    }

  private Instruction RewriteMethodReference(Instruction source, MethodReference methodReference)
    {
        var declaringType = methodReference.DeclaringType;
        if (IsSourceBuilderType(declaringType))
        {
            var targetMethod = FindMatchingMethodOnTargetBuilder(methodReference);
            if (targetMethod != null)
            {
                return Instruction.Create(source.OpCode, _module.ImportReference(targetMethod));
            }
        }

        if (declaringType.FullName == TaskFullName || declaringType.Name == "Task")
        {
            if (methodReference.Name is "get_CompletedTask" or "FromResult")
            {
                var targetType = methodReference.Name == "FromResult" && _targetResultType != null
                    ? GetTargetTaskTypeWithResult()
                    : _targetReturnType;
                var resolved = targetType.Resolve();
                if (resolved != null)
                {
                    var candidate = resolved.Methods.FirstOrDefault(
                        m => m.IsStatic && m.Name == methodReference.Name);
                    if (candidate != null)
                    {
                        return Instruction.Create(source.OpCode, _module.ImportReference(candidate));
                    }
                }
            }
        }

        return Instruction.Create(source.OpCode, _module.ImportReference(methodReference));
    }

    private Instruction RewriteFieldReference(Instruction source, FieldReference fieldReference)
    {
        if (fieldReference.Name == BuilderFieldName && IsSourceBuilderType(fieldReference.DeclaringType))
        {
            var targetField = FindBuilderFieldOnTargetStateMachine(fieldReference);
            if (targetField != null)
            {
                return Instruction.Create(source.OpCode, targetField);
            }
        }

        return Instruction.Create(source.OpCode, _module.ImportReference(fieldReference));
    }

    private MethodReference? FindMatchingMethodOnTargetBuilder(MethodReference sourceMethod)
    {
        var targetBuilderDef = _targetBuilderType.Resolve();
        if (targetBuilderDef == null)
        {
            return null;
        }

        foreach (var method in targetBuilderDef.Methods)
        {
            if (method.Name != sourceMethod.Name)
            {
                continue;
            }

            if (method.HasThis != sourceMethod.HasThis)
            {
                continue;
            }

            if (method.Parameters.Count != sourceMethod.Parameters.Count)
            {
                continue;
            }

            return _module.ImportReference(method);
        }

        return null;
    }

    private FieldReference? FindBuilderFieldOnTargetStateMachine(FieldReference sourceField)
    {
        var declaring = sourceField.DeclaringType.Resolve();
        if (declaring == null)
        {
            return null;
        }

        foreach (var field in declaring.Fields)
        {
            if (field.Name == BuilderFieldName)
            {
                return _module.ImportReference(field);
            }
        }

        return null;
    }

    private TypeReference GetTargetTaskTypeWithResult()
    {
        if (_targetResultType == null)
        {
            return _targetReturnType;
        }

        var resolved = _targetReturnType.Resolve();
        if (resolved == null || !resolved.HasGenericParameters)
        {
            return _targetReturnType;
        }

        return new GenericInstanceType(_targetReturnType)
        {
            GenericArguments = { _targetResultType },
        };
    }

    private static bool TryResolveBuilderType(
        TypeDefinition stateMachineType,
        out TypeReference builderType,
        out string? error)
    {
        builderType = null!;
        error = null;

        foreach (var field in stateMachineType.Fields)
        {
            if (field.Name != BuilderFieldName)
            {
                continue;
            }

            builderType = field.FieldType;
            return true;
        }

        error = $"State machine '{stateMachineType.FullName}' is missing the {BuilderFieldName} field.";
        return false;
    }

    private bool IsTaskType(TypeReference typeReference)
    {
        var fullName = typeReference.FullName;
        return fullName == TaskFullName
            || (typeReference is GenericInstanceType generic
                && generic.ElementType.FullName == TaskFullName);
    }

    private bool IsSourceBuilderType(TypeReference typeReference)
    {
        var fullName = typeReference.FullName;
        return fullName.StartsWith(TaskBuilderFullName, StringComparison.Ordinal)
            || typeReference.Name.StartsWith("AsyncTaskMethodBuilder", StringComparison.Ordinal)
            || typeReference.FullName == _sourceBuilderType.FullName;
    }

    private const string WeaveHoistedFieldPrefix = "<>w_";

    /// <summary>
    /// Merges hoisted fields referenced by the template prefix/postfix into the target state machine.
    /// Template await slots (<c>&lt;&gt;u__N</c>) and builder/state fields are not merged; other fields are renamed with a <c>&lt;&gt;w_</c> prefix to avoid conflicts with target compiler numbering.
    /// </summary>
    public Dictionary<FieldDefinition, FieldDefinition> MergeHoistedFields(
        TypeDefinition templateStateMachine,
        TypeDefinition targetStateMachine)
    {
        var map = new Dictionary<FieldDefinition, FieldDefinition>();
        foreach (var templateField in templateStateMachine.Fields)
        {
            if (ShouldSkipTemplateHoistedField(templateField))
            {
                continue;
            }

            var fieldName = AllocateWeaveHoistedFieldName(templateField.Name, targetStateMachine);
            var fieldType = RewriteTypeReference(templateField.FieldType);
            var newField = new FieldDefinition(fieldName, templateField.Attributes, fieldType);
            targetStateMachine.Fields.Add(newField);
            map[templateField] = newField;
        }

        return map;
    }

    private static bool ShouldSkipTemplateHoistedField(FieldDefinition templateField)
    {
        if (templateField.Name is BuilderFieldName or StateFieldName)
        {
            return true;
        }

        if (templateField.IsStatic)
        {
            return true;
        }

        // 仅用于模板 await 标记区间，前缀/后缀 IL 不会引用。
        if (templateField.Name.StartsWith("<>u__", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string AllocateWeaveHoistedFieldName(string templateFieldName, TypeDefinition targetStateMachine)
    {
        var baseName = WeaveHoistedFieldPrefix + templateFieldName;
        if (targetStateMachine.Fields.All(field => field.Name != baseName))
        {
            return baseName;
        }

        for (var suffix = 0; suffix < int.MaxValue; suffix++)
        {
            var candidate = $"{WeaveHoistedFieldPrefix}{suffix}_{templateFieldName}";
            if (targetStateMachine.Fields.All(field => field.Name != candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("无法为编织提升字段分配唯一名称。");
    }
}
