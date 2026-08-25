namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Why a template reference was not followed (Requirement 5.3).
/// </summary>
/// <remarks>
/// Every one of these ends with the template shown on the canvas as unexpanded, naming itself and
/// saying why. A diagram that quietly drops a template is worse than one that admits the gap.
/// </remarks>
public enum PipelineTemplateUnresolvedReason
{
    /// <summary>It was followed. Present so the enum has a defined zero rather than an accidental one.</summary>
    None,

    /// <summary>
    /// It names a repository resource - <c>path@repo</c> - whose contents this module has no way
    /// to read, since the repository is not checked out here.
    /// </summary>
    OtherRepository,

    /// <summary>
    /// Its path leaves the workspace, whether by <c>..</c> or by being absolute. Refused rather
    /// than followed: a diagram must not become a way to read arbitrary files off the machine.
    /// </summary>
    OutsideWorkspace,

    /// <summary>
    /// Its path contains an expression, so which file it means depends on parameters this module
    /// does not evaluate.
    /// </summary>
    ParameterDependent,

    /// <summary>The path is inside the workspace, but there is no file there.</summary>
    NotFound,

    /// <summary>The file is there but could not be read or is not YAML this can parse.</summary>
    Unreadable,

    /// <summary>Following it would return to a template already being expanded.</summary>
    Cyclic,
}
