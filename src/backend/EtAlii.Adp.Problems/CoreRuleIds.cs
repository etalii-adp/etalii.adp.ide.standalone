namespace EtAlii.Adp.Problems;

/// <summary>
/// The rule ids core reaches for when no module has judged the file - the ids
/// <see cref="ProblemCollector.AddCore"/> stamps.
/// </summary>
/// <remarks>
/// They are named here rather than written as literals at each site because the store has to
/// recognise two of them: a core verdict carries no rules version, so the store cannot ask a
/// module whether the verdict still holds, and it has to know which core verdicts depend on
/// something that can change underneath them.
/// </remarks>
internal static class CoreRuleIds
{
    /// <summary>The file could not be read at all.</summary>
    public const string Unreadable = "core.unreadable";

    /// <summary>No known diagram type claims the file's MIME type.</summary>
    public const string UnknownType = "core.unknown-type";

    /// <summary>More than one known diagram type claims the file's extension.</summary>
    public const string AmbiguousExtension = "core.ambiguous-extension";

    /// <summary>A module's validator timed out or threw.</summary>
    public const string ValidatorFailed = "core.validator-failed";

    /// <summary>
    /// Whether the verdict is a claim about the diagram-type REGISTRY rather than about the
    /// file: that nothing claims this type, or that too much does. Both are answered by the
    /// router, and both stop being true when the set of known types changes - while the file
    /// they name does not move, so no file stamp can carry the news.
    /// </summary>
    public static bool IsAboutRouting(string ruleId) =>
        ruleId is UnknownType or AmbiguousExtension;
}
