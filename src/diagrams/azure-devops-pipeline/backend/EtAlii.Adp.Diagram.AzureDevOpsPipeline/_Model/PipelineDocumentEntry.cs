using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// One loaded pipeline: the document, what it parsed to, and - where it did not parse - why.
/// </summary>
/// <remarks>
/// The failure is carried rather than thrown because a pipeline that does not parse still has to
/// open: the diagram shows as unavailable naming the file and line, which is what tells the user
/// where to go, and every edit is withheld so a broken file is never made worse (Requirement 3.6).
/// </remarks>
/// <param name="Document">The lines, exactly as read.</param>
/// <param name="Model">What the file says; empty when it could not be read.</param>
/// <param name="Error">Why it could not be parsed; empty when it could.</param>
/// <param name="ErrorLine">The line the parser stopped at, 1-based; 0 when there was no error.</param>
public sealed record PipelineDocumentEntry(
    LineDocument Document,
    PipelineModel Model,
    string Error,
    int ErrorLine)
{
    /// <summary>Whether the file parsed, and so whether it may be edited.</summary>
    public bool IsUsable => Error.Length == 0;
}
