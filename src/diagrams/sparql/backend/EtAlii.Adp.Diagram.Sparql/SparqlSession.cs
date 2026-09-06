using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using Serilog;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One open query diagram for one connection. Core owns the stream, the authorization and the
/// connection lifetime; this owns the query reading and expresses every change as a
/// <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <para>
/// Repositioning is the only mutating gesture this module has, and it never touches the
/// <c>.rq</c>: it dispatches the core <see cref="SetRegistrationLayoutCommand"/>, which writes
/// the <c>.adp</c>'s <c>layout:</c> block. The query file has no writer anywhere in this module,
/// so its byte-identical round trip holds by construction rather than by care.
/// </para>
/// <para>
/// <see cref="UpdateView"/> narrows what this connection holds to what its viewport admits:
/// what newly falls inside is added, what left is removed (view-delta-adoption Requirements
/// 1.2 and 1.3). The change path renders through the same viewport, so an ordinary edit never
/// re-sends the elements the viewport just culled.
/// </para>
/// </remarks>
public sealed class SparqlSession : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<SparqlSession>();

    private readonly string _bodyPath;
    private readonly string? _registrationPath;
    private readonly ISparqlDocumentStore _documents;
    private readonly SparqlElementMapper _mapper;

    /// <summary>The project's history, so a reposition is one undo away. Null makes even that unavailable.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>What this connection was last sent, so a change can be diffed against it.</summary>
    private IReadOnlyList<DiagramElement> _delivered = [];

    /// <summary>
    /// What this connection is looking at. Unbounded until the client reports, so a connection
    /// that never reports keeps the whole query it was given at baseline.
    /// </summary>
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public SparqlSession(
        string bodyPath,
        string? registrationPath,
        ISparqlDocumentStore documents,
        SparqlElementMapper mapper,
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
        var before = Render().Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        _viewport = viewport;
        var after = Render();

        // Add for what appeared, then Remove for what left - the order every adopting session
        // emits in, and the opposite of what Requirement 4.3 anticipated.
        var appeared = after.Where(element => !before.Contains(element.Id)).ToArray();
        var departed = before.Except(after.Select(element => element.Id), StringComparer.Ordinal).ToArray();

        var deltas = new List<DiagramDelta>();
        if (appeared.Length > 0)
        {
            deltas.Add(new DiagramAddDelta(appeared));
        }

        if (departed.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(departed));
        }

        _delivered = after;
        return deltas;
    }

    /// <summary>Refused: a query's structure comes from its text, so nothing here is re-parented by dragging.</summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            "A query's structure comes from its text, so nothing here can be moved into a different group. Dragging changes where an element sits on the canvas.");
    }

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - the module's single mutating gesture, and one that leaves the query untouched
    /// (Requirement 5.1, 6.3).
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
            // Registering the file lifts this (Requirement 5.3).
            return "This query was opened without a registration, so there is nowhere to store a position. Register the file to arrange it.";
        }

        var refusal = Refusal(elementId);
        if (refusal.Length > 0)
        {
            return refusal;
        }

        var result = await _history.ExecuteAsync(
            new SetRegistrationLayoutCommand(_registrationPath, elementId, x, y),
            cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    /// <summary>Why this element cannot carry an authored position, or empty when it can (Requirement 5.4).</summary>
    private static string Refusal(string elementId)
    {
        if (elementId.StartsWith("anon:", StringComparison.Ordinal))
        {
            return "That is an anonymous variable - written as a blank node, it has no name to key a stored position by, so it takes its computed place.";
        }

        if (elementId.StartsWith("edge:", StringComparison.Ordinal))
        {
            return "An edge is drawn between its endpoints; move one of those instead.";
        }

        if (elementId.StartsWith("note:", StringComparison.Ordinal))
        {
            return "An annotation stays with what it constrains; move that instead.";
        }

        if (elementId == SparqlElementMapper.HeaderId)
        {
            return "The header band states the query's form and modifiers, and stays at the top of the diagram.";
        }

        if (elementId == SparqlElementMapper.TruncationId)
        {
            return "That banner reports the truncation and is not something this diagram can move.";
        }

        return "";
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<DiagramElement> Render()
    {
        var entry = _documents.GetOrLoad(_bodyPath);
        var projection = SparqlProjection.Project(entry.Model);
        var stored = _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        // Layout applies the stored positions itself, because a region anchor moves its whole
        // frame rather than one element - which the element-by-element overlay could not express.
        var layout = SparqlLayout.Compute(projection, stored);

        // Through the viewport, always. When this rendered unfiltered while UpdateView
        // filtered, an ordinary edit re-sent every element the viewport had just culled and
        // the two paths disagreed about what the client held.
        return _mapper.Visible(projection, layout, _viewport);
    }

    private void OnDocumentChanged(object? sender, SparqlDocumentChangedEventArgs args)
    {
        if (!string.Equals(args.Path, _bodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
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
            // A file that vanished or locked mid-reload costs this notification, never the
            // session: the next change tries again.
            _logger.Warning(exception, "Could not re-read {BodyPath} after a change", _bodyPath);
        }
    }
}
