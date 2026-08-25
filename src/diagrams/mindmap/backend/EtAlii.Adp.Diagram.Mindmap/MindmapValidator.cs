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
/// A link whose target file is missing is deliberately not judged here: the
/// <see cref="IDiagramValidator"/> seam hands over the document text alone, with no location
/// on disk, so a map-relative link cannot be resolved to a file - a rule for it needs a
/// wider seam first.
/// </remarks>
public sealed class MindmapValidator : IDiagramValidator
{
    public DiagramOrigin Origin => Diagram.Mindmap.Origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        string document, string baseName, CancellationToken cancellationToken)
    {
        var problems = new List<DiagramProblem>();

        MindmapDocument map;
        try
        {
            // Read-only judgement: no id assignment, so the document is taken exactly as
            // the file has it.
            map = MindmapDocument.Parse(document, assignMissingIds: false);
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
