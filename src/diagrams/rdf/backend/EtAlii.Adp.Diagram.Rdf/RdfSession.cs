using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
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
/// <see cref="UpdateView"/> answers with nothing new because the whole diagram is delivered at
/// open: the budget bounds what is drawn, so there is nothing left to virtualize.
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
        _ = viewport;
        return [];
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
        var entry = _documents.GetOrLoad(_bodyPath);
        var projection = RdfProjection.Project(entry.Model);
        var stored = _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        var positions = RegistrationLayout.Apply(RdfLayout.Positions(projection), stored);
        return _mapper.Elements(projection, positions);
    }

    private void OnDocumentChanged(object? sender, RdfDocumentChangedEventArgs args)
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
