using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Serilog;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One open RDF diagram for one connection. Core owns the stream, the authorization and the
/// connection lifetime; this owns the data-graph reading and expresses every change as a
/// <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <para>
/// A drag never touches the RDF file: repositioning dispatches the core
/// <see cref="SetRegistrationLayoutCommand"/>, which writes the <c>.adp</c>'s <c>layout:</c>
/// block keyed by <c>res:{iri}</c> ids - safe in the block's grammar because IRIs cannot
/// contain spaces (Requirement 4).
/// </para>
/// <para>
/// <see cref="UpdateView"/> answers with what has come into view and takes back what has left.
/// This is the module where viewport filtering pays for itself: the drawn-node budget alone
/// discards by document order, so a reader of a large ontology could never reach past the first
/// thousand resources however far they panned. The budget survives as a floor against a
/// pathological view (Requirement 5.4), not as the means of keeping the document drawable.
/// </para>
/// <para>
/// The whole document is projected and laid out whatever the viewport says, and only then
/// filtered. A layout computed over the visible set would move every node as the reader panned,
/// because the bands pack by what is in them.
/// </para>
/// </remarks>
public sealed class RdfSession : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<RdfSession>();

    private readonly string _bodyPath;
    private readonly string? _registrationPath;
    private readonly IRdfDocumentStore _documents;
    private readonly RdfElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the diagram read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>What this connection was last sent, so a change can be diffed against it.</summary>
    private IReadOnlyList<DiagramElement> _delivered = [];

    /// <summary>What this connection last said it can see. Everything, until it says otherwise.</summary>
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    /// <summary>
    /// The whole document's projection and layout, kept between reports. A pan is a viewport
    /// change and not a document change, so re-projecting and re-laying out an ontology of
    /// thousands of resources on every settled pan would be work with no new answer. Dropped
    /// whenever the document changes, which is the only thing that can invalidate it.
    /// </summary>
    private (RdfProjectionResult Projection, IReadOnlyDictionary<string, RegistrationPosition> Positions)? _laidOut;

    public RdfSession(
        string bodyPath,
        string? registrationPath,
        IRdfDocumentStore documents,
        RdfElementMapper mapper,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _bodyPath = bodyPath;
        _registrationPath = registrationPath;
        _documents = documents;
        _mapper = mapper;
        _history = history;
        _documents.Changed += OnDocumentChanged;
    }

    /// <inheritdoc />
    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = Render();
        _delivered = elements;

        return elements.Count > 0
            ? [new DiagramAddDelta(elements)]
            : [];
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _viewport = viewport;

        var current = Render();
        var deltas = DiagramDiff.Between(_delivered, current);
        _delivered = current;

        return deltas;
    }

    /// <summary>Refused: nothing in this diagram nests under a parent - a resource's place is a position.</summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("A resource has no parent to move it under; dragging changes where it sits on the canvas.");
    }

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - and never writes the RDF file (Requirement 4.1).
    /// </summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        if (_registrationPath is not { Length: > 0 })
        {
            // Registering the file lifts this (Requirement 4.3).
            return "This diagram was opened without a registration, so there is nowhere to store a position. Register the file to arrange it.";
        }

        if (elementId.StartsWith("edge:", StringComparison.Ordinal) || elementId == RdfElementMapper.TruncationId)
        {
            return "That element is not something this diagram can move.";
        }

        if (elementId.StartsWith("blank:", StringComparison.Ordinal))
        {
            // The identity boundary: a blank node's ordinal is this parse's alone, so a stored
            // position would silently jump to another node on the next reparse (Requirement 4.5).
            return "That is a blank node, whose identity does not survive a reparse, so a stored position could not be trusted. Name it with an IRI to arrange it.";
        }

        var result = await _history.ExecuteAsync(
            new SetRegistrationLayoutCommand(_registrationPath, elementId, x, y),
            cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<DiagramElement> Render()
    {
        var (projection, positions) = LaidOut();
        return _mapper.Elements(InView(projection, positions), positions);
    }

    /// <summary>
    /// The whole document, projected and positioned, cached until the document changes.
    /// Projected without a budget on purpose: a resource the reader can pan to needs a
    /// position, and it can only have a stable one in a layout of everything.
    /// </summary>
    private (RdfProjectionResult Projection, IReadOnlyDictionary<string, RegistrationPosition> Positions) LaidOut()
    {
        if (_laidOut is { } cached)
        {
            return cached;
        }

        var entry = _documents.GetOrLoad(_bodyPath);
        var projection = RdfProjection.Project(entry.Model, int.MaxValue);
        var stored = _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        var positions = RegistrationLayout.Apply(RdfLayout.Positions(projection), stored);
        var result = (projection, positions);
        _laidOut = result;

        return result;
    }

    /// <summary>
    /// What the reported viewport admits: the nodes whose reserved cell it intersects, the edges
    /// both of whose endpoints survived, and the budget applied to that set rather than to the
    /// document.
    /// </summary>
    /// <remarks>
    /// The counts handed on are the <em>document's</em>, not this view's, and that is deliberate.
    /// <see cref="RdfSelection.IsTruncated" /> decides read-only from the document alone, with no
    /// viewport to consult, so a banner derived from the view would contradict it - a file could
    /// be read-only with nothing on screen saying why, or say it is truncated while every edit
    /// was allowed. Reach improves; what a document may do does not change.
    /// </remarks>
    private RdfProjectionResult InView(
        RdfProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        var visible = projection.Nodes
            .Where(node => RdfViewport.Admits(_viewport, positions, node.Id, RdfLayout.CellWidth, RdfLayout.CellHeight))
            .ToList();

        // The floor: only a view holding more than the whole budget is cut, and then in document
        // order, so the same view always yields the same nodes.
        var truncated = projection.Total > RdfProjection.DefaultBudget;
        var kept = visible.Count > RdfProjection.DefaultBudget
            ? visible.Take(RdfProjection.DefaultBudget).ToList()
            : visible;

        var keptIds = kept.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = projection.Edges
            .Where(edge => keptIds.Contains(edge.FromId) && keptIds.Contains(edge.ToId))
            .ToList();

        return new RdfProjectionResult(
            kept,
            edges,
            truncated ? RdfProjection.DefaultBudget : kept.Count,
            truncated ? projection.Total : kept.Count);
    }



    private void OnDocumentChanged(object? sender, RdfDocumentChangedEventArgs args)
    {
        if (!string.Equals(args.Path, _bodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            _laidOut = null;
            var current = Render();
            var deltas = DiagramDiff.Between(_delivered, current);
            _delivered = current;

            if (deltas.Count > 0)
            {
                Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that vanished or locked mid-reload costs this notification, never the
            // session: the next change tries again.
            _logger.Warning(exception, "Could not re-read {BodyPath} after a change", _bodyPath);
        }
    }
}
