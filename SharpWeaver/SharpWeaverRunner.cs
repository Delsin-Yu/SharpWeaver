using Mono.Cecil;

namespace SharpWeaver;

/// <summary>IL post-processor run options.</summary>
public sealed class SharpWeaverOptions
{
    /// <summary>Path to the assembly to be woven.</summary>
    public string? AssemblyPath { get; init; }

    /// <summary>Semicolon-separated reference assembly paths.</summary>
    public string? References { get; init; }

    /// <summary>Whether to only resolve weave bindings without writing IL.</summary>
    public bool DryRun { get; init; }

    /// <summary>Whether to output verbose diagnostic information.</summary>
    public bool Verbose { get; init; }
}

/// <summary>IL post-processor dry-run and discovery phase orchestration.</summary>
public static class SharpWeaverRunner
{
    /// <summary>Executes weave discovery and (optionally) dry-run output.</summary>
    /// <param name="options">Run options.</param>
    /// <returns>Process exit code: 0 on success, non-zero on failure.</returns>
    public static int Run(SharpWeaverOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AssemblyPath))
        {
            Console.Error.WriteLine("错误：必须通过 --assembly 指定待编织程序集路径。");
            return 1;
        }

        if (!File.Exists(options.AssemblyPath))
        {
            Console.Error.WriteLine($"错误：找不到程序集 '{options.AssemblyPath}'。");
            return 1;
        }

        var referencePaths = ParseReferences(options.References);
        if (options.Verbose)
        {
            Console.WriteLine($"Assembly: {options.AssemblyPath}");
            Console.WriteLine($"References: {referencePaths.Count} path(s)");
            foreach (var path in referencePaths)
            {
                Console.WriteLine($"  - {path}");
            }

            Console.WriteLine($"Dry run: {options.DryRun}");
        }

        ReferenceAssemblyResolver resolver;
        try
        {
            resolver = new ReferenceAssemblyResolver(options.AssemblyPath, referencePaths);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：加载程序集失败：{ex.Message}");
            return 1;
        }

        using (resolver)
        {
            var wovenAssembly = resolver.WovenAssembly;

            WeaveScanner.Scan(wovenAssembly, out var weaves, out var weaveScanErrors);

            var allErrors = new List<string>(weaveScanErrors);

            var plannerResult = MethodWeavePlanner.Plan(weaves, resolver, wovenAssembly.MainModule);
            allErrors.AddRange(plannerResult.Errors);

            if (options.DryRun)
            {
                if (allErrors.Count > 0)
                {
                    foreach (var error in allErrors)
                    {
                        Console.Error.WriteLine(error);
                    }

                    return 1;
                }

                Console.WriteLine(
                    $"SharpWeaver dry-run: {plannerResult.Plans.Count} method(s) with weave plan(s)");
                foreach (var plan in plannerResult.Plans)
                {
                    PrintMethodWeavePlan(plan);
                }

                return 0;
            }

            if (allErrors.Count > 0)
            {
                foreach (var error in allErrors)
                {
                    Console.Error.WriteLine(error);
                }

                return 1;
            }

            var weaveResult = AssemblyWeaver.Weave(
                resolver.WovenAssembly,
                plannerResult.Plans,
                resolver,
                options.Verbose);
            if (!weaveResult.Success)
            {
                foreach (var error in weaveResult.Errors)
                {
                    Console.Error.WriteLine(error);
                }

                return 1;
            }

            if (options.Verbose)
            {
                Console.WriteLine($"SharpWeaver: woven {weaveResult.MethodsWoven} method(s).");
            }
            else
            {
                Console.WriteLine($"SharpWeaver: woven {weaveResult.MethodsWoven} method(s) into '{options.AssemblyPath}'.");
            }

            return 0;
        }
    }

    private static void PrintMethodWeavePlan(MethodWeavePlan plan)
    {
        Console.WriteLine($"[Weave] Method: {MethodSignatureFormatter.Format(plan.Method)}");
        foreach (var weave in plan.Weaves)
        {
            Console.WriteLine(
                $"  Weave (priority={weave.Priority}): {weave.WeaveMethodDisplayName} [{weave.TargetSignature}]");
        }

        foreach (var callSite in plan.CallSiteMatches)
        {
            Console.WriteLine(
                $"  CallSite: {callSite.CalledMethod.FullName}");
            foreach (var weave in callSite.Weaves)
            {
                Console.WriteLine(
                    $"    WeaveCallSite (priority={weave.Priority}): {weave.WeaveMethodDisplayName} [{weave.TargetSignature}]");
            }
        }
    }

    private static List<string> ParseReferences(string? references)
    {
        if (string.IsNullOrWhiteSpace(references))
        {
            return [];
        }

        return references
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => path.Length > 0)
            .ToList();
    }
}
