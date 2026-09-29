namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Which outcome of what it waits for lets an element run (Requirement 6.7).
/// </summary>
/// <remarks>
/// An edge that only fires on failure is a different arrow from one that fires on success, and
/// that difference is one of the things a pipeline file is worst at showing. These are the forms
/// Azure documents; anything else is <see cref="Custom"/> rather than guessed at.
/// </remarks>
public enum PipelineEdgeCondition
{
    /// <summary>The default: it runs when everything it depends on succeeded.</summary>
    OnSuccess,

    /// <summary><c>always()</c> - it runs whatever happened, cancellation included.</summary>
    Always,

    /// <summary><c>failed()</c> - it runs only when what it depends on failed.</summary>
    OnFailure,

    /// <summary>
    /// <c>succeededOrFailed()</c> - it runs whether or not what it depends on succeeded, but not
    /// if the run was cancelled. Distinct from <see cref="Always"/>, which is the point of it.
    /// </summary>
    OnSuccessOrFailure,

    /// <summary>A condition this module will not interpret, carried verbatim for the reader.</summary>
    Custom,
}
