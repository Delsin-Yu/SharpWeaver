using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Weave result.</summary>
public sealed class AssemblyWeaveResult
{
    /// <summary>Creates a weave result.</summary>
    /// <param name="methodsWoven">Number of successfully woven methods.</param>
    /// <param name="errors">List of error messages.</param>
    public AssemblyWeaveResult(int methodsWoven, IReadOnlyList<string> errors)
    {
        MethodsWoven = methodsWoven;
        Errors = errors;
    }

    /// <summary>Number of successfully woven methods.</summary>
    public int MethodsWoven { get; }

    /// <summary>List of error messages.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Whether all operations succeeded (no errors).</summary>
    public bool Success => Errors.Count == 0;
}

/// <summary>Weaves ILWeaving plans into the target assembly and writes it back to disk (preserving portable PDB).</summary>
public static class AssemblyWeaver
{
    /// <summary>
    /// Applies all ILWeaving weave plans to the loaded target assembly and writes it back to disk.
    /// </summary>
    /// <param name="wovenAssembly">Target assembly loaded in ReadWrite mode.</param>
    /// <param name="plans">List of weave plans grouped by method.</param>
    /// <param name="resolver">Assembly resolver (used for exact mode target resolution validation).</param>
    /// <param name="verbose">Whether to output verbose diagnostic information.</param>
    /// <returns>Weave result.</returns>
    public static AssemblyWeaveResult Weave(
        AssemblyDefinition wovenAssembly,
        IReadOnlyList<MethodWeavePlan> plans,
        IAssemblyResolver resolver,
        bool verbose = false)
    {
        var errors = new List<string>();
        var assemblyPath = wovenAssembly.MainModule.FileName;
        var pdbPath = GetPdbPath(assemblyPath);
        var wovenOuterMethods = new HashSet<MethodDefinition>();

        foreach (var plan in plans)
        {
            var spliceMethod = plan.Method;

            if (!WeaveMethodFilter.HasWeaveableBody(spliceMethod))
            {
                if (verbose)
                {
                    Console.WriteLine(
                        $"跳过 ILWeaving '{MethodSignatureFormatter.Format(spliceMethod)}'（abstract/extern/无方法体）。");
                }

                continue;
            }

            var overrideWoven = true;
            foreach (var weave in plan.Weaves)
            {
                var validationTarget = ResolveValidationTarget(weave, plan, resolver);
                if (validationTarget == null)
                {
                    errors.Add($"ILWeaving 编织 '{weave.WeaveMethodDisplayName}'：无法解析精确目标方法。");
                    overrideWoven = false;
                    break;
                }

                if (!WeaveSignatureValidator.TryValidate(
                        weave,
                        validationTarget,
                        spliceMethod,
                        plan.OuterMethod,
                        out var validationError))
                {
                    errors.Add(validationError!);
                    overrideWoven = false;
                    break;
                }

                string? spliceError;
                var spliced = weave.IsAsync
                    ? AsyncWeaveSplicer.TrySplice(weave, spliceMethod, plan.OuterMethod, out spliceError)
                    : WeaveSplicer.TrySplice(weave, spliceMethod, plan.OuterMethod, out spliceError);

                if (!spliced)
                {
                    errors.Add(spliceError!);
                    overrideWoven = false;
                    break;
                }
            }

            if (overrideWoven)
            {
                wovenOuterMethods.Add(plan.OuterMethod);
                if (verbose)
                {
                    Console.WriteLine(
                        $"已 ILWeaving 编织 '{MethodSignatureFormatter.Format(plan.OuterMethod)}'。");
                }
            }
        }

        var methodsWoven = wovenOuterMethods.Count;

        if (errors.Count > 0)
        {
            return new AssemblyWeaveResult(methodsWoven, errors);
        }

        TryWriteAssembly(wovenAssembly, pdbPath);
        return new AssemblyWeaveResult(methodsWoven, errors);
    }

    private static void TryWriteAssembly(AssemblyDefinition wovenAssembly, string pdbPath)
    {
        var writerParameters = new WriterParameters
        {
            WriteSymbols = File.Exists(pdbPath),
        };

        try
        {
            wovenAssembly.Write(writerParameters);
        }
        catch (Exception ex) when (writerParameters.WriteSymbols && IsBrokenDebugInformationException(ex))
        {
            File.Delete(pdbPath);
            wovenAssembly.Write(new WriterParameters { WriteSymbols = false });
        }
    }

    private static bool IsBrokenDebugInformationException(Exception exception) =>
        exception is NotSupportedException
        || exception is ArgumentNullException { ParamName: "instruction" };

    private static MethodDefinition? ResolveValidationTarget(
        WeaveInfo weave,
        MethodWeavePlan plan,
        IAssemblyResolver resolver)
    {
        if (weave.Pattern is ExactSignaturePattern exact)
        {
            if (!resolver.TryResolveMethod(exact.Parsed, out var targetMethod, out _))
            {
                return null;
            }

            return targetMethod;
        }

        return plan.OuterMethod;
    }

    private static string GetPdbPath(string assemblyPath)
    {
        return Path.ChangeExtension(assemblyPath, ".pdb");
    }
}
