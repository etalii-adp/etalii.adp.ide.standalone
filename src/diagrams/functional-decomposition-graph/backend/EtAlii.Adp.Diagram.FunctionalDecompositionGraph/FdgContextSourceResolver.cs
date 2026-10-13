using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Resolves an element or connection of an <c>.fdg</c> document to a selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Brought forward from task 13 to land with task 11</b> (the user's chat ruling of 2026-09-25):
/// registering task 11's session factory makes the host's <c>DrawnConnections</c> guard open FDG's
/// example, and every connection it draws must resolve to a selection.
/// </para>
/// <para>
/// <b>The element a selection describes is the one the canvas was sent.</b> Its payload comes from
/// <see cref="FdgElementMapper"/> rather than being assembled here, so the stream and the selection
/// can never describe one element two ways. The element resolved is also the mapper's: the FIRST
/// entry with an id, so a document repeating an id selects what the canvas drew.
/// </para>
/// <para>
/// <b>A placement or a finished connect gesture resolves too</b> (task 13): <c>new:x,y</c> names the
/// element a drop is about to create and <c>rel:from-&gt;to</c> the connection a gesture proposes,
/// through a channel with one element id per call and no field for a position. Each resolves to a
/// target the action provider reads back out, and lives for one execution: never selected, tracked
/// or written.
/// </para>
/// </remarks>
public sealed class FdgContextSourceResolver : DiagramElementSourceResolver<FdgModel>
{
    private readonly IFdgDocumentStore _documents;
    private readonly FdgElementMapper _mapper;

    public FdgContextSourceResolver(DiagramFileRouter router, IFdgDocumentStore documents, FdgElementMapper mapper)
        : base(router)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        _documents = documents;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.FunctionalDecompositionGraph.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a functional decomposition graph.",
        "That element is no longer in this graph.",
        "New element",
        "New connection");

    /// <inheritdoc />
    protected override FdgModel Load(string bodyPath) => _documents.GetOrLoad(bodyPath).Model;

    /// <inheritdoc />
    protected override DiagramElement? Drawn(FdgModel model, string elementId) =>
        _mapper.Visible(model, DiagramViewport.Unbounded).FirstOrDefault(candidate => candidate.Id == elementId);

    /// <inheritdoc />
    protected override IDisposable Follow(ShortGuid watchId, string bodyPath, string elementId, Action changed) =>
        FollowStore(_documents, bodyPath, changed);

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(FdgModel model, string elementId)
    {
        // The first entry with the id, element before connection, as the mapper draws it.
        var element = model.Elements.FirstOrDefault(candidate => candidate.Id == elementId);
        if (element is not null)
        {
            var text = element.IsComment ? FirstLineOf(element.Text, "Comment") : Named(element.Name, element.Id);
            return ([text], text);
        }

        var connection = model.Connections.FirstOrDefault(candidate => candidate.Id == elementId);
        if (connection is not null)
        {
            var text = connection.Name.Length > 0 ? connection.Name : $"{connection.From} → {connection.To}";
            return ([text], text);
        }

        return null;
    }

    private static string Named(string name, string id) => name.Length > 0 ? name : id;

    private static string FirstLineOf(string text, string otherwise)
    {
        var first = text.Split('\n', 2)[0].TrimEnd('\r').Trim();
        return first.Length > 0 ? first : otherwise;
    }
}
