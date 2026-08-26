namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// The rule identifiers this module reports problems under.
/// </summary>
/// <remarks>
/// Named in one place because a rule id outlives its message: it is what a user filters on, what
/// a log names, and what a future suppression would key on. Changing a message is editing prose;
/// changing an id is breaking something.
/// </remarks>
public static class PipelineRules
{
    /// <summary>A <c>dependsOn</c> naming a stage or job that is not there.</summary>
    public const string DanglingDependency = "azure-pipeline.dangling-dependency";

    /// <summary>Elements that wait for each other, directly or round a longer loop.</summary>
    public const string Cycle = "azure-pipeline.cycle";

    /// <summary>An element nothing can ever reach, because what it waits for can never finish.</summary>
    public const string Unreachable = "azure-pipeline.unreachable";

    /// <summary>No stage has an empty dependency set, so there is nothing for the run to begin with.</summary>
    public const string NoStartingStage = "azure-pipeline.no-starting-stage";

    /// <summary>An element with neither a name nor a display name.</summary>
    public const string Unnamed = "azure-pipeline.unnamed";

    /// <summary>A template this module can see it will never be able to follow.</summary>
    public const string TemplateNotFollowed = "azure-pipeline.template-not-followed";
}
