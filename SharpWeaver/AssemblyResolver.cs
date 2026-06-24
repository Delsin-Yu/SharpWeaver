using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharpWeaver;

/// <summary>Resolves assemblies, types, and methods based on reference paths provided by MSBuild.</summary>
public sealed class ReferenceAssemblyResolver : BaseAssemblyResolver, IAssemblyResolver, IDisposable
{
    private readonly Dictionary<string, AssemblyDefinition> _assemblyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _assemblyPathByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AssemblyDefinition> _loadedAssemblies = [];
    private readonly AssemblyDefinition _wovenAssembly;

    /// <summary>Creates a reference resolver and loads the assembly to be woven.</summary>
    /// <param name="wovenAssemblyPath">Path to the assembly to be woven.</param>
    /// <param name="referencePaths">List of reference assembly paths.</param>
    public ReferenceAssemblyResolver(string wovenAssemblyPath, IEnumerable<string> referencePaths)
    {
        var wovenFullPath = Path.GetFullPath(wovenAssemblyPath);

        foreach (var path in referencePaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(path);
            var fileName = Path.GetFileNameWithoutExtension(fullPath);
            _assemblyPathByName.TryAdd(fileName, fullPath);
        }

        _wovenAssembly = LoadAssembly(wovenFullPath, isWovenAssembly: true);

        foreach (var path in _assemblyPathByName.Values)
        {
            if (!string.Equals(path, wovenFullPath, StringComparison.OrdinalIgnoreCase))
            {
                LoadAssembly(path);
            }
        }
    }

    /// <summary>Assembly definition of the assembly to be woven.</summary>
    public AssemblyDefinition WovenAssembly => _wovenAssembly;

    /// <summary>All loaded assemblies (including the assembly to be woven).</summary>
    public IReadOnlyList<AssemblyDefinition> LoadedAssemblies => _loadedAssemblies;

    /// <inheritdoc />
    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        var key = name.Name;
        if (_assemblyCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_assemblyPathByName.TryGetValue(key, out var path))
        {
            return LoadAssembly(path);
        }

        return base.Resolve(name);
    }

    /// <inheritdoc />
    public bool TryResolveType(string fullTypeName, out TypeDefinition? type, out string? error)
    {
        type = null;
        error = null;

        if (TryFindTypeInLoadedAssemblies(fullTypeName, out type))
        {
            return true;
        }

        ExpandLoadedAssembliesFromReferences();

        if (TryFindTypeInLoadedAssemblies(fullTypeName, out type))
        {
            return true;
        }

        error = $"无法解析类型 '{fullTypeName}'：在待编织程序集与引用列表中均未找到该类型。";
        return false;
    }

    /// <inheritdoc />
    public bool TryResolveMethod(ParsedSignature signature, out MethodDefinition? method, out string? error)
    {
        method = null;
        error = null;

        if (!TryResolveType(signature.TypeFullName, out var declaringType, out var typeError))
        {
            error = $"签名 '{signature.RawSignature}'：{typeError}";
            return false;
        }

        var candidateMethods = declaringType!.Methods
            .Where(candidate => candidate.Name == signature.MethodName && !candidate.HasGenericParameters)
            .ToList();

        if (candidateMethods.Count == 0)
        {
            error =
                $"签名 '{signature.RawSignature}'：在类型 '{signature.TypeFullName}' 上未找到名为 '{signature.MethodName}' 的方法。";
            return false;
        }

        var resolvedParamTypes = new TypeReference[signature.ParameterTypeNames.Count];
        for (var i = 0; i < signature.ParameterTypeNames.Count; i++)
        {
            var paramTypeName = signature.ParameterTypeNames[i];
            if (!TryResolveTypeReference(paramTypeName, out var paramType, out var paramError))
            {
                error = $"签名 '{signature.RawSignature}'：无法解析第 {i + 1} 个参数类型 '{paramTypeName}'。{paramError}";
                return false;
            }

            resolvedParamTypes[i] = paramType!;
        }

        foreach (var candidate in candidateMethods)
        {
            if (candidate.Parameters.Count != resolvedParamTypes.Length)
            {
                continue;
            }

            var allMatch = true;
            for (var i = 0; i < resolvedParamTypes.Length; i++)
            {
                if (!TypeReferenceMatches(candidate.Parameters[i].ParameterType, resolvedParamTypes[i]))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                method = candidate;
                return true;
            }
        }

        var expectedParams = string.Join(
            ", ",
            signature.ParameterTypeNames.Select(SignatureParser.FormatTypeNameForDisplay));
        error =
            $"签名 '{signature.RawSignature}'：在类型 '{signature.TypeFullName}' 上未找到匹配参数 ({expectedParams}) 的方法 '{signature.MethodName}'。";
        return false;
    }

    private AssemblyDefinition LoadAssembly(string path, bool isWovenAssembly = false)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (_assemblyCache.TryGetValue(fileName, out var cached))
        {
            return cached;
        }

        var pdbPath = Path.ChangeExtension(path, ".pdb");
        var readerParameters = new ReaderParameters
        {
            AssemblyResolver = this,
            ReadingMode = isWovenAssembly ? ReadingMode.Immediate : ReadingMode.Deferred,
            ReadWrite = isWovenAssembly,
            ReadSymbols = isWovenAssembly && File.Exists(pdbPath),
        };

        var assembly = AssemblyDefinition.ReadAssembly(path, readerParameters);
        _assemblyCache[assembly.Name.Name] = assembly;
        _loadedAssemblies.Add(assembly);
        return assembly;
    }

    private bool TryResolveTypeReference(string fullTypeName, out TypeReference? type, out string? error)
    {
        type = null;
        error = null;

        if (TryGetPrimitiveTypeReference(fullTypeName, out type))
        {
            return true;
        }

        if (!TryResolveType(fullTypeName, out var typeDefinition, out var typeError))
        {
            error = typeError;
            return false;
        }

        type = _wovenAssembly.MainModule.ImportReference(typeDefinition);
        return true;
    }

    private bool TryGetPrimitiveTypeReference(string fullTypeName, out TypeReference? type)
    {
        var typeSystem = _wovenAssembly.MainModule.TypeSystem;
        type = fullTypeName switch
        {
            "System.Void" => typeSystem.Void,
            "System.Boolean" => typeSystem.Boolean,
            "System.Byte" => typeSystem.Byte,
            "System.SByte" => typeSystem.SByte,
            "System.Int16" => typeSystem.Int16,
            "System.UInt16" => typeSystem.UInt16,
            "System.Int32" => typeSystem.Int32,
            "System.UInt32" => typeSystem.UInt32,
            "System.Int64" => typeSystem.Int64,
            "System.UInt64" => typeSystem.UInt64,
            "System.Char" => typeSystem.Char,
            "System.Single" => typeSystem.Single,
            "System.Double" => typeSystem.Double,
            "System.String" => typeSystem.String,
            "System.Object" => typeSystem.Object,
            _ => null,
        };

        return type != null;
    }

    private bool TryFindTypeInLoadedAssemblies(string fullTypeName, out TypeDefinition? type)
    {
        foreach (var assembly in _loadedAssemblies)
        {
            var found = FindTypeInModule(assembly.MainModule, fullTypeName);
            if (found != null)
            {
                type = found;
                return true;
            }
        }

        type = null;
        return false;
    }

    private void ExpandLoadedAssembliesFromReferences()
    {
        var seen = new HashSet<string>(
            _loadedAssemblies.Select(assembly => assembly.Name.Name),
            StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<AssemblyNameReference>();

        foreach (var assembly in _loadedAssemblies.ToArray())
        {
            foreach (var reference in assembly.MainModule.AssemblyReferences)
            {
                if (seen.Add(reference.Name))
                {
                    pending.Enqueue(reference);
                }
            }
        }

        while (pending.Count > 0)
        {
            var reference = pending.Dequeue();
            try
            {
                var resolved = Resolve(reference);
                foreach (var childReference in resolved.MainModule.AssemblyReferences)
                {
                    if (seen.Add(childReference.Name))
                    {
                        pending.Enqueue(childReference);
                    }
                }
            }
            catch (AssemblyResolutionException)
            {
            }
        }
    }

    private static TypeDefinition? FindTypeInModule(ModuleDefinition module, string fullTypeName)
    {
        foreach (var type in module.EnumerateAllTypes())
        {
            if (type.FullName == fullTypeName)
            {
                return type;
            }
        }

        return null;
    }

    private static bool TypeReferenceMatches(TypeReference left, TypeReference right)
    {
        if (left.IsByReference != right.IsByReference)
        {
            return false;
        }

        var leftElement = left.IsByReference ? ((ByReferenceType)left).ElementType : left;
        var rightElement = right.IsByReference ? ((ByReferenceType)right).ElementType : right;

        return leftElement.FullName == rightElement.FullName;
    }

    /// <summary>Disposes loaded assembly definitions.</summary>
    public new void Dispose()
    {
        foreach (var assembly in _loadedAssemblies)
        {
            assembly.Dispose();
        }

        _loadedAssemblies.Clear();
        _assemblyCache.Clear();
        Dispose(true);
    }
}
