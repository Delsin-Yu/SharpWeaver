using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Plans scanned weave methods into per-method, priority-sorted weave plans.</summary>
public static class MethodWeavePlanner
{
    /// <summary>
    /// Builds per-method weave plans from the weave list and target module.
    /// </summary>
    /// <param name="weaves">Scanned weave method list.</param>
    /// <param name="resolver">Assembly resolver.</param>
    /// <param name="wovenModule">Target module to be woven.</param>
    /// <returns>Planning result.</returns>
    public static MethodWeavePlannerResult Plan(
        IReadOnlyList<WeaveInfo> weaves,
        IAssemblyResolver resolver,
        ModuleDefinition wovenModule)
    {
        var errors = new List<string>();
        var matches = new Dictionary<MethodDefinition, WeavePlanMatch>();
        var weaveTemplateCallees = WeaveTemplateCalleeCollector.Collect(weaves, wovenModule);

        foreach (var weave in weaves)
        {
            switch (weave.Pattern)
            {
                case ExactSignaturePattern exact:
                    if (!ExactWeaveMatcher.TryMatch(
                            weave,
                            exact.Parsed,
                            resolver,
                            wovenModule,
                            matches,
                            out var exactError))
                    {
                        errors.Add(exactError!);
                    }

                    break;

                case WildcardSignaturePatternWrapper wildcard:
                    WildcardWeaveMatcher.Match(weave, wildcard.Parsed, wovenModule, matches, weaveTemplateCallees);
                    break;
            }
        }

        var plans = new List<MethodWeavePlan>();
        foreach (var pair in matches.OrderBy(
                     entry => MethodSignatureFormatter.Format(entry.Key),
                     StringComparer.Ordinal))
        {
            var entry = pair.Value;
            var sortedWeaves = entry.Weaves
                .OrderBy(w => w.Priority)
                .ThenBy(w => w.DiscoveryOrder)
                .ToList();

            plans.Add(new MethodWeavePlan(
                entry.SpliceMethod,
                sortedWeaves,
                entry.ResolvedTargetMethod,
                entry.OuterMethod));
        }

        return new MethodWeavePlannerResult(plans, errors);
    }
}
