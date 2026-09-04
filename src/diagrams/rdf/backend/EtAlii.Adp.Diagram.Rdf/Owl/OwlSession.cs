using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
using Serilog;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One open ontology diagram for one connection - the <c>w3c/owl</c> reading over the family's
/// one store, so an edit through any reading of the file is visible here and vice versa
/// (Requirement 8.4).
/// </summary>
/// <remarks>
/// The move refusals extend the family's with the expression case: an <c>expr:</c> id (and a
/// materialized anchor's <c>thing:</c>/<c>dt:</c> id) is deterministic within a parse and
/// deliberately unstable across edits, so a stored position would silently jump to another
/// structure on the next reparse - the blank-node identity boundary (Requirement 3.2).
/// </remarks>
public sealed class OwlSession : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<OwlSession>();

    /// <summary>
    /// The cell a node occupies for the purpose of deciding whether it is on screen. The
    /// reading draws both small shapes and tall class cards, so this is the generous case:
    /// over-inclusion costs one extra element on the wire, while under-inclusion is a hole the
    /// reader looks straight at.
    /// </summary>
    private const double CellWidth = 240;

    /// <inheritdoc cref="CellWidth" />
    private const double CellHeight = 320;

    private readonly string _bodyPath;
    private readonly string? _registrationPath;
    private readonly IRdfDocumentStore _documents;
    private readonly OwlElementMapper _mapper;
    private readonly RdfElementMapper _differ;
    private readonly IHistoryStack? _history;

    private IReadOnlyList<DiagramElement> _delivered = [];

    /// <summary>What this connection last said it can see. Everything, until it says otherwise.</summary>
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    /// <summary>
    /// The whole ontology, projected and positioned, kept between reports: a pan changes the
    /// viewport and not the document. Dropped when the document changes, which is the only
    /// thing that can invalidate it.
    /// </summary>
    private (OwlGraphResult Graph, IReadOnlyDictionary<string, RegistrationPosition> Positions)? _laidOut;

    public OwlSession(
        string bodyPath,
        string? registrationPath,
        IRdfDocumentStore documents,
        OwlElementMapper mapper,
        RdfElementMapper differ,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(differ);

        _bodyPath = bodyPath;
        _registrationPath = registrationPath;
        _documents = documents;
        _mapper = mapper;
        _differ = differ;
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
        var deltas = _differ.Diff(_delivered, current);
        _delivered = current;

        return deltas;
    }

    /// <summary>Refused: nothing in this diagram nests under a parent - an element's place is a position.</summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("An ontology element has no parent to move it under; dragging changes where it sits on the canvas.");
    }

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block for IRI-derived
    /// ids, and refuses everything the identity boundary governs - never writing the ontology
    /// file either way (Requirements 3.2, 4.1).
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
            return "This diagram was opened without a registration, so there is nowhere to store a position. Register the file to arrange it.";
        }

        if (elementId.StartsWith("edge:", StringComparison.Ordinal) || elementId == OwlElementMapper.TruncationId)
        {
            return "That element is not something this diagram can move.";
        }

        if (elementId.StartsWith("expr:", StringComparison.Ordinal))
        {
            return "That is an anonymous class expression, whose identity does not survive an edit to the file, so a stored position could not be trusted. It takes its place beside the class that uses it; edit the expression as text.";
        }

        if (!OwlLayout.IsPositionable(elementId))
        {
            return "That element is drawn where its property needs it and cannot be arranged on its own.";
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
        var (graph, positions) = LaidOut();
        return _mapper.Elements(InView(graph, positions), positions);
    }

    /// <summary>
    /// The whole ontology, projected and positioned, cached until it changes. Projected without
    /// a budget on purpose: a class the reader can pan to needs a position, and it can only
    /// have a stable one in a layout of everything.
    /// </summary>
    private (OwlGraphResult Graph, IReadOnlyDictionary<string, RegistrationPosition> Positions) LaidOut()
    {
        if (_laidOut is { } cached)
        {
            return cached;
        }

        var entry = _documents.GetOrLoad(_bodyPath);
        var graph = OwlProjection.Project(entry.Model, int.MaxValue);
        var stored = _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        var result = (graph, OwlLayout.Apply(OwlLayout.Positions(graph), stored));
        _laidOut = result;

        return result;
    }

    /// <summary>
    /// What the reported viewport admits: the nodes it intersects, the edges both of whose
    /// endpoints survived, and the budget applied to that set rather than to the document.
    /// </summary>
    /// <remarks>
    /// This is the reading the whole specification was argued from. An ontology of more than
    /// 1,600 resources opened behind a first-N banner that discarded by document order, so
    /// panning could never recover the rest - a strictly worse answer to the same problem than
    /// asking what the reader is looking at. The counts handed on are still the document's,
    /// because <see cref="OwlGraphResult.Truncated" /> drives the banner and the read-only
    /// refusal and both are decided from the document with no viewport to consult.
    /// </remarks>
    private OwlGraphResult InView(
        OwlGraphResult graph, IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        var visible = graph.Nodes
            .Where(node => RdfViewport.Admits(_viewport, positions, node.Id, CellWidth, CellHeight))
            .ToList();

        var truncated = graph.Total > RdfProjection.DefaultBudget;
        var kept = visible.Count > RdfProjection.DefaultBudget
            ? visible.Take(RdfProjection.DefaultBudget).ToList()
            : visible;

        var keptIds = kept.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = graph.Edges
            .Where(edge => keptIds.Contains(edge.FromId) && keptIds.Contains(edge.ToId))
            .ToList();

        return new OwlGraphResult(
            kept,
            edges,
            truncated ? RdfProjection.DefaultBudget : kept.Count,
            truncated ? graph.Total : kept.Count);
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
            var deltas = _differ.Diff(_delivered, current);
            _delivered = current;

            if (deltas.Count > 0)
            {
                Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not re-read {BodyPath} after a change", _bodyPath);
        }
    }
}
