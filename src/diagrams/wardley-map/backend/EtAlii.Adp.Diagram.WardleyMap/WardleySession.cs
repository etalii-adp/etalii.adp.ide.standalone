using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Serilog;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// One open Wardley map for one connection (Requirement 10.8).
/// </summary>
/// <remarks>
/// <para>
/// Core owns the stream, the authorization and the connection lifetime. This owns what the map
/// <em>is</em> and expresses every change as a <see cref="DiagramDelta"/>.
/// </para>
/// <para>
/// <b>It filters nothing.</b> <see cref="UpdateView"/> answers with the whole map and is the
/// shortest correct implementation of that method in the repository, deliberately: a Wardley
/// map is a bounded space holding tens of elements, so viewport filtering has nothing to do
/// here. `adp-diagram-ide` Requirement 4.3's virtualization is a capability core offers, not an
/// obligation every module owes, and a filter written only to satisfy a rule would be code
/// nobody needs and everybody has to read (Requirement 10.5).
/// </para>
/// </remarks>
public sealed class WardleySession : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<WardleySession>();

    private readonly string _bodyPath;
    private readonly IWardleyDocumentStore _documents;
    private readonly WardleyElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the map read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>What this connection was last sent, so a change can be diffed against it.</summary>
    private IReadOnlyList<DiagramElement> _delivered = [];

    public WardleySession(
        string bodyPath,
        IWardleyDocumentStore documents,
        WardleyElementMapper mapper,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _bodyPath = bodyPath;
        _documents = documents;
        _mapper = mapper;
        _history = history;
        _documents.Changed += OnDocumentChanged;
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = Render();
        _delivered = elements;

        // One message rather than a stream of per-element ones: the whole map is delivered
        // anyway, so there is nothing to page (Requirement 10.5, and the Performance section).
        var deltas = new List<DiagramDelta>();
        if (elements.Count > 0)
        {
            deltas.Add(new DiagramAddDelta(elements));
        }

        deltas.AddRange(Groups());
        return deltas;
    }

    /// <summary>
    /// Answers with the whole map, whatever the viewport says. See the class remarks: this is
    /// short on purpose.
    /// </summary>
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        //ArgumentNullException.ThrowIfNull(viewport);

        // Nothing changes with the viewport, so there is nothing to send. A connection that has
        // already been given the map does not need it again for panning.
        return [];
    }

    /// <summary>
    /// Refused. A Wardley element's place in the document is not a containment - dragging one
    /// changes where it sits on the map, which is <see cref="MoveElementToAsync"/>.
    /// </summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("Dragging a component changes where it sits on the map, not what contains it.");
    }

    /// <summary>
    /// Moves an element to a position (Requirement 7.2). The canvas point is converted back
    /// into the document's own axes here, through the one function that may do it, and
    /// dispatched as a command so the change is undoable and reaches every other connection.
    /// </summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This map is read-only.";
        }

        var coordinate = WardleyAxis.Clamp(WardleyAxis.ToCoordinate(x, y));
        var result = await _history.ExecuteAsync(
            new MoveWardleyElementCommand(_bodyPath, elementId, coordinate.Visibility, coordinate.Maturity),
            cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    /// <summary>The map as elements, with identities reconciled against what the sidecar records.</summary>
    private IReadOnlyList<DiagramElement> Render()
    {
        var map = WardleyParser.Parse(_documents.GetOrLoad(_bodyPath));
        return _mapper.Elements(map, _documents.Identities(_bodyPath));
    }

    private IReadOnlyList<DiagramDelta> Groups()
    {
        var map = WardleyParser.Parse(_documents.GetOrLoad(_bodyPath));
        return _mapper.Group(map, _documents.Identities(_bodyPath));
    }

    /// <summary>
    /// A save from any connection, or an edit made outside ADP, arrives here and goes out as the
    /// difference from what this connection was last sent (Requirement 10.7).
    /// </summary>
    private void OnDocumentChanged(object? sender, WardleyDocumentChangedEventArgs args)
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
