using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Wildcard signature mode ILWeaving target enumeration and matching.</summary>
public static class WildcardWeaveMatcher
{
    /// <summary>
    /// Enumerates weaveable methods in the target assembly and adds wildcard-matching weaves to the candidate table.
    /// </summary>
    /// <param name="weave">Weave information.</param>
    /// <param name="wildcardPattern">Wildcard pattern.</param>
    /// <param name="wovenModule">Target module to be woven.</param>
    /// <param name="matches">Output: splice method → weave plan match entries.</param>
    /// <param name="weaveTemplateCallees">Set of methods directly called by weave templates, to exclude from wildcard matching.</param>
    public static void Match(
        WeaveInfo weave,
        WildcardSignaturePattern wildcardPattern,
        ModuleDefinition wovenModule,
        Dictionary<MethodDefinition, WeavePlanMatch> matches,
        IReadOnlySet<MethodDefinition> weaveTemplateCallees)
    {
        foreach (var type in wovenModule.EnumerateAllTypes())
        {
            foreach (var method in type.Methods)
            {
                var isCandidate = weave.IsAsync
                    ? WeaveMethodFilter.IsAsyncWeaveCandidate(method, weaveTemplateCallees)
                    : WeaveMethodFilter.IsSyncWeaveCandidate(method, weave, weaveTemplateCallees);

                if (!isCandidate)
                {
                    continue;
                }

                if (weave.GenericWeave != WeaveMethodFilter.RequiresGenericWeave(method))
                {
                    continue;
                }

                if (!WildcardSignatureMatcher.IsMatch(wildcardPattern, method))
                {
                    continue;
                }

                if (WeaveExclusionMatcher.IsExcluded(weave, method))
                {
                    continue;
                }

                if (weave.IsAsync)
                {
                    if (!AsyncMethodHelper.TryResolveMoveNext(method, out var moveNext, out _))
                    {
                        continue;
                    }

                    WeaveMatchRegistry.AddWeave(matches, moveNext, method, method, weave);
                }
                else
                {
                    WeaveMatchRegistry.AddWeave(matches, method, method, method, weave);
                }
            }
        }
    }
}
