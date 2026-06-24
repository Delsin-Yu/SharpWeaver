using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace SharpWeaver;

/// <summary>Matches a <see cref="WildcardSignaturePattern"/> against a <see cref="MethodDefinition"/>.</summary>
public static class WildcardSignatureMatcher
{
    /// <summary>Determines whether a method matches a wildcard signature pattern.</summary>
    /// <param name="pattern">Wildcard pattern.</param>
    /// <param name="method">Candidate method.</param>
    /// <returns>Returns <see langword="true"/> if it matches.</returns>
    public static bool IsMatch(WildcardSignaturePattern pattern, MethodDefinition method)
    {
        var declaringType = method.DeclaringType;
        var namespaceSegments = GetEffectiveNamespaceSegments(declaringType);
        if (!SegmentMatcher.MatchSequence(pattern.NamespaceSegments, namespaceSegments))
        {
            return false;
        }

        if (!SegmentMatcher.MatchName(pattern.TypeName, declaringType.Name))
        {
            return false;
        }

        if (!SegmentMatcher.MatchName(pattern.MethodName, method.Name))
        {
            return false;
        }

        var candidateParams = new string[method.Parameters.Count];
        for (var i = 0; i < method.Parameters.Count; i++)
        {
            candidateParams[i] = SignatureParser.FormatTypeNameForDisplay(
                method.Parameters[i].ParameterType.FullName);
        }

        return ParameterListMatcher.Match(pattern.Parameters, candidateParams);
    }

    private static IReadOnlyList<string> GetEffectiveNamespaceSegments(TypeDefinition declaringType)
    {
        if (!declaringType.IsNested)
        {
            return SegmentMatcher.SplitNamespace(declaringType.Namespace);
        }

        var parentNames = new Stack<string>();
        var current = declaringType.DeclaringType;
        while (current != null)
        {
            parentNames.Push(current.Name);
            current = current.DeclaringType;
        }

        var rootNamespace = declaringType.DeclaringType;
        while (rootNamespace?.DeclaringType != null)
        {
            rootNamespace = rootNamespace.DeclaringType;
        }

        var segments = SegmentMatcher.SplitNamespace(rootNamespace?.Namespace).ToList();
        segments.AddRange(parentNames);
        return segments;
    }
}
