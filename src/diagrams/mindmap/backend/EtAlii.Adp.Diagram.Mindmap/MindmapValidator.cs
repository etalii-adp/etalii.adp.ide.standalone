using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The mindmap type's rules, judged against the document text through the module's own
/// parser: a map that will not parse, a central topic with no text, and two nodes claiming
/// one id. What each rule reports is what a Freeplane user would call wrong, in their
/// terms - which is why an ordinary node with no text is <i>not</i> judged: Freeplane
/// itself keeps such nodes (the reference fixture carries one), so they are style, not
/// problems.
/// </summary>
/// <remarks>
/// A link whose target file is missing is still not judged here, but the reason has changed
/// and is worth recording. The <see cref="IDiagramValidator"/> seam used to hand over the
/// document text alone, so a map-relative link could not be resolved to a file at all; it now
/// carries <see cref="DiagramValidationRequest.BodyPath"/> and
/// <see cref="DiagramValidationRequest.RootPath"/>, so the rule has become possible
/// (ansible-structure-diagram widened the seam for its own folder-subject rules). What stops
/// it now is only that it belongs to the mindmap specification rather than to the one that
/// happened to widen the seam - it is a missing rule, no longer a missing capability.
/// </remarks>
public sealed class MindmapValidator : IDiagramValidator
{
    public DiagramOrigin Origin => Diagram.Mindmap.Origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseName = request.BaseName;
        var problems = new List<DiagramProblem>();

        MindmapDocument map;
        try
        {
            // Read-only judgement: no id assignment, so the document is taken exactly as
            // the file has it.
            map = MindmapDocument.Parse(request.Document, assignMissingIds: false);
        }
        catch (MindmapFormatException exception)
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Error,
                $"'{baseName}' is not a readable mind map: {exception.Message}",
                "mindmap.not-a-map"));
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(problems);
        }

        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(map.Root, node =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node.IsRoot && node.Text.Trim().Length == 0)
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"The map '{baseName}' has an unnamed central topic.",
                    "mindmap.unnamed-root",
                    LocationOf(node)));
            }

            if (node.Id.Length > 0)
            {
                if (seen.TryGetValue(node.Id, out var firstText))
                {
                    // Two nodes with one id would answer to each other's selections.
                    problems.Add(new DiagramProblem(
                        DiagramProblemSeverity.Error,
                        $"Two nodes share the id '{node.Id}' ('{firstText}' and '{node.Text}').",
                        "mindmap.duplicate-id",
                        LocationOf(node)));
                }
                else
                {
                    seen.Add(node.Id, node.Text);
                }
            }
        });

        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(problems);
    }

    private static void Walk(MindmapNode node, Action<MindmapNode> visit)
    {
        visit(node);
        foreach (var child in node.Children)
        {
            Walk(child, visit);
        }
    }

    private static DiagramProblemLocation? LocationOf(MindmapNode node) =>
        node.Id.Length > 0 ? new DiagramProblemElementLocation(node.Id) : null;
}
