using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The mindmap type's rules, judged against the document text: a map that will not parse, a
/// central topic with no text, and two nodes claiming one id. What each rule reports is what a
/// Freeplane user would call wrong, in their terms - which is why an ordinary node with no text is
/// <i>not</i> judged: Freeplane itself keeps such nodes (the reference fixture carries one), so they
/// are style, not problems.
/// </summary>
/// <remarks>
/// <para>
/// <b>The findings are the DISL definition's</b> (<see cref="MindmapDefinition.Findings"/>): its
/// built-in <c>std.unparseable</c> and its two rules over the document's DISL model, read through the
/// FBL binding exactly as the file has it - no id is assigned - in the order of its
/// <c>constraints.order</c>. A finding names the map by the file the Problems panel attributes it to,
/// the registration when there is one, which the definition reads as <c>diagram.file</c>; and a node
/// by the id the file writes, located only when it has one.
/// </para>
/// <para>
/// A link whose target file is missing is still not judged here, but the reason has changed
/// and is worth recording. The <see cref="IDiagramValidator"/> seam used to hand over the
/// document text alone, so a map-relative link could not be resolved to a file at all; it now
/// carries <see cref="DiagramValidationRequest.BodyPath"/> and
/// <see cref="DiagramValidationRequest.RootPath"/>, so the rule has become possible
/// (ansible-structure-diagram widened the seam for its own folder-subject rules). What stops
/// it now is only that it belongs to the mindmap specification rather than to the one that
/// happened to widen the seam - it is a missing rule, no longer a missing capability.
/// </para>
/// </remarks>
public sealed class MindmapValidator : IDiagramValidator
{
    public DiagramOrigin Origin => Diagram.Mindmap.Origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var file = IoPath.GetFileName(request.RegistrationPath ?? request.BodyPath);
        IReadOnlyList<DiagramProblem> problems =
        [
            .. MindmapDefinition.Findings(request.Document, file).Select(finding => new DiagramProblem(
                Severity(finding.Severity),
                finding.Message,
                finding.Code,
                LocationOf(finding))),
        ];

        return ValueTask.FromResult(problems);
    }

    private static DiagramProblemLocation? LocationOf(DislFinding finding) =>
        finding.ElementIds.FirstOrDefault() is { Length: > 0 } id ? new DiagramProblemElementLocation(id) : null;

    private static DiagramProblemSeverity Severity(string severity) => severity switch
    {
        "error" => DiagramProblemSeverity.Error,
        "info" or "hint" => DiagramProblemSeverity.Info,
        _ => DiagramProblemSeverity.Warning,
    };
}
