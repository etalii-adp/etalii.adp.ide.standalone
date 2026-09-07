using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using Serilog;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One open scheme diagram for one connection: the projection, the chosen labels and the
/// layered layout composed over the family store, with the authored <c>layout:</c> overlay on
/// top (skos-diagram Requirements 1-4). Everything the family session promises holds here - the
/// SKOS file is never touched by a drag, and every refusal names its reason.
/// </summary>
public sealed class SkosSession : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<SkosSession>();

    /// <summary>
    /// The cell an element occupies for the purpose of deciding whether it is on screen - the
    /// layout's own column and row pitch. Over-inclusion is the safe direction: one extra
    /// element on the wire against a hole the reader looks straight at.
    /// </summary>
    private const double CellWidth = 240;

    /// <inheritdoc cref="CellWidth" />
    private const double CellHeight = 110;

    private readonly string _bodyPath;
    private readonly string? _registrationPath;
    private readonly IRdfDocumentStore _documents;
    private readonly SkosElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the diagram read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>What this connection was last sent, so a change can be diffed against it.</summary>
    private IReadOnlyList<DiagramElement> _delivered = [];

    /// <summary>What this connection last said it can see. Everything, until it says otherwise.</summary>
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    /// <summary>
    /// The whole vocabulary, projected and positioned, kept between reports: a pan changes the
    /// viewport and not the document. Dropped when the document changes, which is the only
    /// thing that can invalidate it.
    /// </summary>
    private (SkosProjectionResult Projection, IReadOnlyDictionary<string, RegistrationPosition> Positions)? _laidOut;

    public SkosSession(
        string bodyPath,
        string? registrationPath,
        string displayLanguage,
        IRdfDocumentStore documents,
        SkosElementMapper mapper,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLanguage);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _bodyPath = bodyPath;
        _registrationPath = registrationPath;
        DisplayLanguage = displayLanguage.ToLowerInvariant();
        _documents = documents;
        _mapper = mapper;
        _history = history;
        _documents.Changed += OnDocumentChanged;
    }

    /// <inheritdoc />
    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    /// <summary>The language the chooser was seeded with - what the canvas compares chips against.</summary>
    public string DisplayLanguage { get; }

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
        var deltas = _mapper.Diff(_delivered, current);
        _delivered = current;

        return deltas;
    }

    /// <summary>Refused: filing a concept under a parent is the broader gesture, not a tree move.</summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("A concept is filed under a parent with the hierarchy gesture; dragging changes where it sits on the canvas.");
    }

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - and never writes the SKOS file (Requirement 4.3).
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

        if (elementId.StartsWith("edge:", StringComparison.Ordinal) || elementId == RdfElementMapper.TruncationId)
        {
            return "That element is not something this diagram can move.";
        }

        if (elementId.StartsWith("blank:", StringComparison.Ordinal))
        {
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
        return _mapper.Elements(InView(projection, positions), positions, DisplayLanguage);
    }

    /// <summary>
    /// The whole vocabulary, projected and positioned, cached until it changes. Projected
    /// without a budget on purpose: a concept the reader can pan to needs a position, and it
    /// can only have a stable one in a layout of everything - the hierarchy is laid out by
    /// walking it, so a layout of the visible set would rearrange as the reader moved.
    /// </summary>
    private (SkosProjectionResult Projection, IReadOnlyDictionary<string, RegistrationPosition> Positions) LaidOut()
    {
        if (_laidOut is { } cached)
        {
            return cached;
        }

        var entry = _documents.GetOrLoad(_bodyPath);
        var projection = SkosProjection.Project(entry.Model, int.MaxValue);
        var layout = SkosLayout.Layout(projection);
        var stored = _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        var result = (projection, RegistrationLayout.Apply(layout.Positions, stored));
        _laidOut = result;

        return result;
    }

    /// <summary>
    /// What the reported viewport admits: the schemes, concepts and collections it intersects,
    /// the edges both of whose endpoints survived, and the budget applied to that set rather
    /// than to the document.
    /// </summary>
    /// <remarks>
    /// The counts handed on are the document's, not this view's, because
    /// <see cref="SkosProjectionResult.Truncated" /> drives the banner and the read-only refusal
    /// and both are decided from the document with no viewport to consult. Reach improves; what
    /// a document may do does not change.
    /// </remarks>
    private SkosProjectionResult InView(
        SkosProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        bool Visible(string id) => RdfViewport.Admits(_viewport, positions, id, CellWidth, CellHeight);

        var schemes = projection.Schemes.Where(scheme => Visible(scheme.Id)).ToList();
        var concepts = projection.Concepts.Where(concept => Visible(concept.Id)).ToList();
        var collections = projection.Collections.Where(collection => Visible(collection.Id)).ToList();

        var truncated = projection.Total > RdfProjection.DefaultBudget;
        var drawn = schemes.Count + concepts.Count + collections.Count;

        var keptIds = schemes.Select(scheme => scheme.Id)
            .Concat(concepts.Select(concept => concept.Id))
            .Concat(collections.Select(collection => collection.Id))
            .ToHashSet(StringComparer.Ordinal);
        var edges = projection.Edges
            .Where(edge => keptIds.Contains(edge.FromId) && keptIds.Contains(edge.ToId))
            .ToList();

        return new SkosProjectionResult(
            schemes,
            concepts,
            collections,
            edges,
            projection.OutOfFileMappings,
            truncated ? RdfProjection.DefaultBudget : drawn,
            truncated ? projection.Total : drawn);
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
            var deltas = _mapper.Diff(_delivered, current);
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
