using EtAlii.Adp.History;
using Serilog;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// One open C4 diagram for one connection: which document, which view within it, and what this
/// connection's viewport shows. Several sessions may share one document - that is the whole of
/// "model once, view many" - so an edit through any of them reaches all of them through the
/// store's change event (c4-diagrams Requirements 1.2, 1.5).
/// </summary>
/// <remarks>
/// <para>
/// A change of any shape - an edit here, an edit through another view, an external save, a
/// viewport report - is answered the same way: the view is rendered again and diffed against what
/// this connection was last given. Following the store, the diff and the dispatch of a drag are the
/// shared session's (code-structure FR-031); what is C4's own is which view it shows.
/// </para>
/// <para>
/// <b>A drag places an element; it never re-parents one.</b> A C4 element's parent is what the
/// document declares - a container belongs to the software system it is written inside - so
/// dropping a container onto another system would be a claim about the architecture, not an
/// arrangement. The position is recorded in the layout sidecar beside the document, because a
/// position is view state and the <c>.dsl</c> is another ecosystem's file (Requirements 3.5, 8.3).
/// </para>
/// </remarks>
public sealed class C4Session : DocumentDiagramSession<C4Document, C4Workspace>
{
    private static readonly ILogger _logger = Log.ForContext<C4Session>();

    private readonly string? _viewKey;
    private readonly IC4DocumentStore _documents;
    private readonly C4ElementMapper _mapper;

    /// <param name="bodyPath">The document this session shows.</param>
    /// <param name="viewKey">The view its <c>.adp</c> named, or null for the document's first.</param>
    /// <param name="documents">The store every C4 session shares.</param>
    /// <param name="mapper">What the view looks like on the canvas.</param>
    /// <param name="history">The project's history, so a drag is one undo away. Null makes the diagram read-only.</param>
    public C4Session(
        string bodyPath,
        string? viewKey,
        IC4DocumentStore documents,
        C4ElementMapper mapper,
        IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        _viewKey = viewKey;
        _documents = documents;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override string ParentRefusal => "Dragging changes where an element is drawn, not what contains it.";

    /// <summary>The view this session shows: the one its <c>.adp</c> named, or the document's first.</summary>
    public C4View? View() => ViewOf(_documents.GetOrLoad(BodyPath).Model);

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(C4Workspace model, DiagramViewport viewport) =>
        ViewOf(model) is { } view ? _mapper.Visible(model, view, viewport, BodyPath) : [];

    /// <summary>A drop records where the user put the element in the view this session shows.</summary>
    protected override DiagramPlacement PlacementOf(C4Workspace model, string elementId, double x, double y) =>
        ViewOf(model) is { } view
            ? DiagramPlacement.By(new MoveC4ElementCommand(BodyPath, view.Key, elementId, x, y))
            : DiagramPlacement.Refused("This diagram has no view to arrange.");

    private C4View? ViewOf(C4Workspace workspace)
    {
        if (workspace.Views.Count == 0)
        {
            return null;
        }

        if (_viewKey is not { Length: > 0 })
        {
            // Requirement 2.6: a document opened without a registration shows its first view.
            return workspace.Views[0];
        }

        var named = workspace.FindView(_viewKey);
        if (named is not null)
        {
            return named;
        }

        _logger.Warning(
            "{BodyPath} declares no view '{ViewKey}'; it declares {Available}",
            BodyPath,
            _viewKey,
            workspace.Views.Select(view => view.Key));
        return null;
    }
}
