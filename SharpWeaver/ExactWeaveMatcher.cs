using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Exact signature mode ILWeaving target resolution and override enumeration.</summary>
public static class ExactWeaveMatcher
{
    /// <summary>
    /// Binds an exact mode weave to override candidates in the target assembly.
    /// </summary>
    /// <param name="weave">Weave information.</param>
    /// <param name="parsed">Parsed exact signature.</param>
    /// <param name="resolver">Assembly resolver.</param>
    /// <param name="wovenModule">Target module to be woven.</param>
    /// <param name="matches">Output: splice method → weave plan match entries.</param>
    /// <param name="error">Error message on failure.</param>
    /// <returns>Returns <see langword="true"/> on success.</returns>
    public static bool TryMatch(
        WeaveInfo weave,
        ParsedSignature parsed,
        IAssemblyResolver resolver,
        ModuleDefinition wovenModule,
        Dictionary<MethodDefinition, WeavePlanMatch> matches,
        out string? error)
    {
        error = null;

        if (!resolver.TryResolveMethod(parsed, out var targetMethod, out var resolveError))
        {
            error = $"ILWeaving 编织 '{weave.WeaveMethodDisplayName}'：{resolveError}";
            return false;
        }

        var overrides = OverrideMatcher.FindOverrides(wovenModule, targetMethod!);
        if (overrides.Count == 0)
        {
            return true;
        }

        foreach (var overrideMethod in overrides)
        {
            if (weave.GenericWeave != WeaveMethodFilter.RequiresGenericWeave(overrideMethod))
            {
                continue;
            }

            if (WeaveExclusionMatcher.IsExcluded(weave, overrideMethod))
            {
                continue;
            }

            if (weave.IsAsync)
            {
                if (!WeaveMethodFilter.IsAsyncWeaveCandidate(overrideMethod))
                {
                    continue;
                }

                if (!AsyncMethodHelper.TryResolveMoveNext(overrideMethod, out var moveNext, out _))
                {
                    continue;
                }

                WeaveMatchRegistry.AddWeave(matches, moveNext, overrideMethod, targetMethod!, weave);
            }
            else
            {
                if (!WeaveMethodFilter.IsSyncWeaveCandidate(overrideMethod, weave))
                {
                    continue;
                }

                WeaveMatchRegistry.AddWeave(matches, overrideMethod, overrideMethod, targetMethod!, weave);
            }
        }

        return true;
    }
}
