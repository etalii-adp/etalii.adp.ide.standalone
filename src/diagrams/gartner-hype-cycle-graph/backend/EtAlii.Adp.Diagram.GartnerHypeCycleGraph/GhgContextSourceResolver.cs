using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Resolves a trend or influence of a <c>.ghg</c> document to a selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>It lands with the session factory</b>, as FDG's did: registering a session factory makes the
/// host's <c>DrawnConnections</c> guard open this module's example, and every influence it draws must
/// resolve to a selection.
/// </para>
/// <para>
/// <b>The element a selection describes is the one the canvas was sent.</b> Its payload comes from
/// <see cref="GhgElementMapper"/> rather than being assembled here, so the stream and the selection
/// can never describe one trend two ways.
/// </para>
/// <para>
/// <b>A placement or a finished connect gesture resolves too</b>: <c>new:x,y</c> names the trend a
/// drop is about to create and <c>rel:from-&gt;to</c> (each end optionally carrying its attachment,
/// see <see cref="GhgGestures"/>) the influence a gesture proposes. Each resolves to a target the
/// action provider reads back out, and lives for one execution: never selected, tracked or written.
/// </para>
/// </remarks>
public sealed class GhgContextSourceResolver : DiagramElementSourceResolver<GhgBody, GhgModel>
{
    private readonly GhgElementMapper _mapper;

    public GhgContextSourceResolver(DiagramFileRouter router, IGhgDocumentStore documents, GhgElementMapper mapper)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.HypeCycleGraph.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a hype cycle graph.",
        "That is no longer in this graph.",
        "New element",
        "New influence");

    /// <inheritdoc />
    protected override DiagramElement? Drawn(GhgModel model, string elementId) =>
        _mapper.Visible(model, DiagramViewport.Unbounded).FirstOrDefault(candidate => candidate.Id == elementId);

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(GhgModel model, string elementId)
    {
        // The first entry with the id, trend, trigger, note, then influence, as the mapper draws it.
        if (model.Trends.FirstOrDefault(candidate => candidate.Id == elementId) is { } trend)
        {
            var text = trend.Name.Length > 0 ? trend.Name : trend.Id;
            return ([text], text);
        }

        if (model.Triggers.FirstOrDefault(candidate => candidate.Id == elementId) is { } trigger)
        {
            var text = trigger.Name.Length > 0 ? trigger.Name : trigger.Id;
            return ([text], text);
        }

        if (model.Notes.FirstOrDefault(candidate => candidate.Id == elementId) is { } note)
        {
            // A note is named by its first line, as a reader would quote it.
            var first = note.Text.Split('\n')[0].Trim();
            var text = first.Length > 0 ? first : "Note";
            return ([text], text);
        }

        if (model.Influences.FirstOrDefault(candidate => candidate.Id == elementId) is { } influence)
        {
            var text = $"{NameOf(model, influence.From)} \u2192 {NameOf(model, influence.To)}";
            return ([text], text);
        }

        return null;
    }

    private static string NameOf(GhgModel model, string id) =>
        model.Trends.FirstOrDefault(t => t.Id == id) is { Name.Length: > 0 } trend ? trend.Name
        : model.Triggers.FirstOrDefault(g => g.Id == id) is { Name.Length: > 0 } trigger ? trigger.Name
        : id;
}
