using Mono.Cecil;

namespace SharpWeaver;

/// <summary>ILWeaving registration result (compatibility layer, delegates to <see cref="MethodWeavePlanner"/>).</summary>
public sealed class WeaveRegistryResult
{
    /// <summary>Creates a registration result.</summary>
    /// <param name="bindings">Successfully bound weaves grouped by target signature (for backward compatibility).</param>
    /// <param name="plans">Weave plans grouped by method.</param>
    /// <param name="errors">List of error messages.</param>
    public WeaveRegistryResult(
        IReadOnlyList<WeaveBinding> bindings,
        IReadOnlyList<MethodWeavePlan> plans,
        IReadOnlyList<string> errors)
    {
        Bindings = bindings;
        Plans = plans;
        Errors = errors;
    }

    /// <summary>Weave bindings grouped by target signature (for backward compatibility with old tests and dry-run).</summary>
    public IReadOnlyList<WeaveBinding> Bindings { get; }

    /// <summary>Weave plans grouped by method.</summary>
    public IReadOnlyList<MethodWeavePlan> Plans { get; }

    /// <summary>List of error messages.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Whether all operations succeeded (no errors).</summary>
    public bool Success => Errors.Count == 0;
}

/// <summary>Binds scanned ILWeaving weave methods to resolved target methods and override candidates to be woven.</summary>
public static class WeaveRegistry
{
    /// <summary>Builds the registry from the weave method list and assembly resolver.</summary>
    /// <param name="weaves">Scanned weave method list.</param>
    /// <param name="resolver">Assembly resolver.</param>
    /// <param name="wovenModule">Target module to be woven.</param>
    /// <returns>Registration result.</returns>
    public static WeaveRegistryResult Build(
        IReadOnlyList<WeaveInfo> weaves,
        IAssemblyResolver resolver,
        ModuleDefinition wovenModule)
    {
        var plannerResult = MethodWeavePlanner.Plan(weaves, resolver, wovenModule);
        var bindings = BuildLegacyBindings(plannerResult.Plans, resolver);
        return new WeaveRegistryResult(bindings, plannerResult.Plans, plannerResult.Errors);
    }

    private static List<WeaveBinding> BuildLegacyBindings(
        IReadOnlyList<MethodWeavePlan> plans,
        IAssemblyResolver resolver)
    {
        var bySignature = new Dictionary<string, MutableWeaveGroup>(StringComparer.Ordinal);

        foreach (var plan in plans)
        {
            foreach (var weave in plan.Weaves)
            {
                if (weave.Pattern is not ExactSignaturePattern exact)
                {
                    continue;
                }

                var sig = weave.TargetSignature;
                if (!bySignature.TryGetValue(sig, out var group))
                {
                    resolver.TryResolveMethod(exact.Parsed, out var targetMethod, out _);
                    group = new MutableWeaveGroup(sig, targetMethod ?? plan.Method);
                    bySignature[sig] = group;
                }

                if (!group.Weaves.Any(w => w.WeaveMethod == weave.WeaveMethod))
                {
                    group.Weaves.Add(weave);
                }

                if (!group.OverrideMethods.Contains(plan.Method))
                {
                    group.OverrideMethods.Add(plan.Method);
                }
            }
        }

        return bySignature.Values
            .OrderBy(g => g.TargetSignature, StringComparer.Ordinal)
            .Select(g => new WeaveBinding(
                g.TargetSignature,
                g.TargetMethod,
                g.Weaves.OrderBy(w => w.Priority).ThenBy(w => w.DiscoveryOrder).ToList(),
                g.OverrideMethods))
            .ToList();
    }

    private sealed class MutableWeaveGroup
    {
        public MutableWeaveGroup(string targetSignature, MethodDefinition targetMethod)
        {
            TargetSignature = targetSignature;
            TargetMethod = targetMethod;
        }

        public string TargetSignature { get; }

        public MethodDefinition TargetMethod { get; }

        public List<WeaveInfo> Weaves { get; } = [];

        public List<MethodDefinition> OverrideMethods { get; } = [];
    }
}
