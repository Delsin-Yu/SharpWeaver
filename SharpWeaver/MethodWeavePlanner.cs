using Mono.Cecil;
using Mono.Cecil.Cil;

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
        var callSiteMatches = new Dictionary<MethodDefinition, List<CallSiteWeaveMatch>>();
        var weaveTemplateCallees = WeaveTemplateCalleeCollector.Collect(weaves, wovenModule);

        foreach (var weave in weaves)
        {
            if (weave.IsCallSite)
            {
                if (!CallSiteWeaveMatcher.TryMatch(
                        weave,
                        resolver,
                        wovenModule,
                        callSiteMatches,
                        weaveTemplateCallees,
                        out var callSiteError))
                {
                    errors.Add(callSiteError!);
                }

                continue;
            }

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

        var plannedMethods = matches.Keys
            .Concat(callSiteMatches.Keys)
            .Distinct()
            .OrderBy(MethodSignatureFormatter.Format, StringComparer.Ordinal)
            .ToList();

        var plans = new List<MethodWeavePlan>();
        foreach (var method in plannedMethods)
        {
            matches.TryGetValue(method, out var entry);

            var sortedWeaves = entry != null
                ? entry.Weaves
                    .OrderBy(w => w.Priority)
                    .ThenBy(w => w.DiscoveryOrder)
                    .ToList()
                : [];

            var sortedCallSites = callSiteMatches.TryGetValue(method, out var methodCallSites)
                ? SortCallSites(method, methodCallSites)
                : [];

            plans.Add(new MethodWeavePlan(
                entry?.SpliceMethod ?? method,
                sortedWeaves,
                entry?.ResolvedTargetMethod ?? method,
                entry?.OuterMethod ?? method,
                sortedCallSites));
        }

        return new MethodWeavePlannerResult(plans, errors);
    }

    private static List<CallSiteWeaveMatch> SortCallSites(
        MethodDefinition method,
        IReadOnlyList<CallSiteWeaveMatch> callSites)
    {
        var instructionIndex = new Dictionary<Instruction, int>();
        for (var i = 0; i < method.Body.Instructions.Count; i++)
        {
            instructionIndex[method.Body.Instructions[i]] = i;
        }

        return callSites
            .OrderBy(callSite => instructionIndex.TryGetValue(callSite.CallInstruction, out var index) ? index : int.MaxValue)
            .Select(callSite =>
            {
                var sortedWeaves = callSite.Weaves
                    .OrderBy(weave => weave.Priority)
                    .ThenBy(weave => weave.DiscoveryOrder)
                    .ToList();
                return new CallSiteWeaveMatch(
                    callSite.CallInstruction,
                    callSite.CalledMethod,
                    callSite.ResolvedCalledMethod,
                    sortedWeaves);
            })
            .ToList();
    }
}
