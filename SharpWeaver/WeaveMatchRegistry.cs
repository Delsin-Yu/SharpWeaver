using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Weave plan match entry: splice target, user-visible outer method, and weave list.</summary>
public sealed class WeavePlanMatch
{
    /// <summary>Creates a weave plan match.</summary>
    /// <param name="spliceMethod">The method that actually receives the spliced IL (outer method for sync, <c>MoveNext</c> for async).</param>
    /// <param name="outerMethod">User-visible method used for metadata capture and validation.</param>
    /// <param name="resolvedTargetMethod">Resolved base/interface method for exact mode; same as <paramref name="outerMethod"/> for wildcard mode.</param>
    /// <param name="weaves">Weave list.</param>
    public WeavePlanMatch(
        MethodDefinition spliceMethod,
        MethodDefinition outerMethod,
        MethodDefinition resolvedTargetMethod,
        List<WeaveInfo> weaves)
    {
        SpliceMethod = spliceMethod;
        OuterMethod = outerMethod;
        ResolvedTargetMethod = resolvedTargetMethod;
        Weaves = weaves;
    }

    /// <summary>The method that actually receives the spliced IL.</summary>
    public MethodDefinition SpliceMethod { get; }

    /// <summary>User-visible outer method.</summary>
    public MethodDefinition OuterMethod { get; }

    /// <summary>Resolved target method for exact mode.</summary>
    public MethodDefinition ResolvedTargetMethod { get; }

    /// <summary>Weave list.</summary>
    public List<WeaveInfo> Weaves { get; }
}

/// <summary>Deduplication helper when adding entries to the per-method weave plan table.</summary>
public static class WeaveMatchRegistry
{
    /// <summary>
    /// Adds a weave to the target method's plan list; skips if the same weave method already exists (for multi-attribute overlapping matches).
    /// </summary>
    /// <param name="matches">Splice method → weave plan match entries.</param>
    /// <param name="spliceMethod">The method that actually receives the spliced IL.</param>
    /// <param name="outerMethod">User-visible outer method.</param>
    /// <param name="resolvedTargetMethod">Resolved target for exact mode; same as <paramref name="outerMethod"/> for wildcard mode.</param>
    /// <param name="weave">Weave to add.</param>
    public static void AddWeave(
        Dictionary<MethodDefinition, WeavePlanMatch> matches,
        MethodDefinition spliceMethod,
        MethodDefinition outerMethod,
        MethodDefinition resolvedTargetMethod,
        WeaveInfo weave)
    {
        if (!matches.TryGetValue(spliceMethod, out var entry))
        {
            entry = new WeavePlanMatch(spliceMethod, outerMethod, resolvedTargetMethod, []);
            matches[spliceMethod] = entry;
        }

        foreach (var existing in entry.Weaves)
        {
            if (ReferenceEquals(existing.WeaveMethod, weave.WeaveMethod))
            {
                return;
            }
        }

        entry.Weaves.Add(weave);
    }
}
