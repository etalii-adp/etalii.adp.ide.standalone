using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// One loaded Databricks configuration file: the document, what it parsed to under each of the
/// family's readings, and - where it did not parse - why.
/// </summary>
/// <remarks>
/// All three readings are carried at once because one file legitimately serves several: a
/// <c>databricks.yml</c> is a bundle that may also declare jobs and pipelines inline, and a
/// resource file holds jobs the bundle diagram counts and the job diagram opens. Each parser
/// simply finds nothing where its structures are absent, so the empty readings cost one pass
/// each over an already-loaded tree. The failure is carried rather than thrown because a file
/// that does not parse still has to open: the diagram shows as unavailable naming the file and
/// line, and every edit is withheld so a broken file is never made worse (Requirement 2.3).
/// </remarks>
/// <param name="Document">The lines, exactly as read.</param>
/// <param name="Bundle">The file read as a bundle; <see cref="BundleModel.Empty"/> where it is not one.</param>
/// <param name="Jobs">Every job the file declares, in file order.</param>
/// <param name="Pipelines">Every pipeline the file declares - or the one a bare settings file is.</param>
/// <param name="Error">Why the file could not be parsed; empty when it could.</param>
/// <param name="ErrorLine">The line the parser stopped at, 1-based; 0 when there was no error.</param>
public sealed record DatabricksDocumentEntry(
    LineDocument Document,
    BundleModel Bundle,
    IReadOnlyList<JobModel> Jobs,
    IReadOnlyList<PipelineModel> Pipelines,
    string Error,
    int ErrorLine)
{
    /// <summary>Whether the file parsed, and so whether it may be edited.</summary>
    public bool IsUsable => Error.Length == 0;
}
