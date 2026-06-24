namespace SharpWeaver;

/// <summary>Fully qualified names of SharpWeaver attributes and marker types used by Cecil scanning.</summary>
internal static class SharpWeaverMetadata
{
    /// <summary>Fully qualified name of <see cref="WeaveAttribute"/>.</summary>
    public const string WeaveAttribute = "SharpWeaver.WeaveAttribute";

    /// <summary>Fully qualified name of <see cref="AsyncWeaveAttribute"/>.</summary>
    public const string AsyncWeaveAttribute = "SharpWeaver.AsyncWeaveAttribute";

    /// <summary>Fully qualified name of <see cref="WeaveExcludeAttribute"/>.</summary>
    public const string WeaveExcludeAttribute = "SharpWeaver.WeaveExcludeAttribute";

    /// <summary>Fully qualified name of <see cref="WeaveExcludeAsyncLikeReturnAttribute"/>.</summary>
    public const string WeaveExcludeAsyncLikeReturnAttribute = "SharpWeaver.WeaveExcludeAsyncLikeReturnAttribute";

    /// <summary>Fully qualified name of <see cref="WeaveMethodNameAttribute"/>.</summary>
    public const string WeaveMethodNameAttribute = "SharpWeaver.WeaveMethodNameAttribute";

    /// <summary>Fully qualified name of <see cref="WeaveTypeNameAttribute"/>.</summary>
    public const string WeaveTypeNameAttribute = "SharpWeaver.WeaveTypeNameAttribute";

    /// <summary>Fully qualified name of <see cref="WeaveLineNumberAttribute"/>.</summary>
    public const string WeaveLineNumberAttribute = "SharpWeaver.WeaveLineNumberAttribute";

    /// <summary>Fully qualified name of <see cref="WeaveFilePathAttribute"/>.</summary>
    public const string WeaveFilePathAttribute = "SharpWeaver.WeaveFilePathAttribute";

    /// <summary>Fully qualified name of <see cref="WeaveTypeParamsAttribute"/>.</summary>
    public const string WeaveTypeParamsAttribute = "SharpWeaver.WeaveTypeParamsAttribute";

    /// <summary>Fully qualified name of <see cref="WeaveTemplate"/>.</summary>
    public const string WeaveTemplate = "SharpWeaver.WeaveTemplate";

    /// <summary>Method name of <see cref="WeaveTemplate.OriginalBody"/>.</summary>
    public const string OriginalBodyMethod = "OriginalBody";

    /// <summary>Method name of <see cref="WeaveTemplate.OriginalBodyAsync"/>.</summary>
    public const string OriginalBodyAsyncMethod = "OriginalBodyAsync";

    /// <summary>Determines whether the attribute type is a sync weave attribute.</summary>
    /// <param name="fullName">Attribute type fully qualified name.</param>
    /// <param name="shortName">Attribute type short name.</param>
    /// <returns>Returns <see langword="true"/> if it is a sync weave attribute.</returns>
    public static bool IsWeaveAttribute(string? fullName, string shortName) =>
        fullName == WeaveAttribute || shortName == nameof(WeaveAttribute);

    /// <summary>Determines whether the attribute type is an async weave attribute.</summary>
    /// <param name="fullName">Attribute type fully qualified name.</param>
    /// <param name="shortName">Attribute type short name.</param>
    /// <returns>Returns <see langword="true"/> if it is an async weave attribute.</returns>
    public static bool IsAsyncWeaveAttribute(string? fullName, string shortName) =>
        fullName == AsyncWeaveAttribute || shortName == nameof(AsyncWeaveAttribute);
}
