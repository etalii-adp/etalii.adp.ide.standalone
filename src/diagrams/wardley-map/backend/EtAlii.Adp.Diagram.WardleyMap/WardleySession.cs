using EtAlii.Adp.Common;
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
/// <b>It filters by viewport</b> (view-delta-adoption Requirement 1). This used to answer
/// <see cref="UpdateView"/> with nothing, on the judgement that a bounded space of tens of
/// elements has nothing to virtualize - reasonable locally, and superseded: the loop is what
/// lets a reader zoomed into one corner of a large map stop paying for the rest, and a module
/// that declines it is a module whose readers cannot.
/// </para>
/// <para>
/// The viewport arrives in the map own 0..1 space, which is this module unit and stays its
/// business; the shared client code converts nothing (Requirement 3.4). What is visible is
/// <see cref="WardleyElementMapper.Visible"/>'s decision, and this class only diffs one answer
/// against the last.
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

    /// <summary>
    /// The rectangle this connection last reported, in the map own 0..1 space. Unbounded until a
    /// report arrives, so a baseline delivers the whole map exactly as it did before this filter
    /// existed - a connection that never reports one is never worse off.
    /// </summary>
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

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
        var elements = Visible();
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
    /// The reader moved: answer with what appeared and what left.
    /// </summary>
    /// <remarks>
    /// Diffed against <see cref="_delivered"/> rather than against a re-rendering under the old
    /// viewport. Both give the same answer while the two agree, and only this one stays right when
    /// they do not - a document change between two reports goes out through
    /// <see cref="OnDocumentChanged"/> and moves what this connection holds, which a recomputation
    /// from the previous rectangle would not know about.
    /// </remarks>
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        var before = _delivered.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        _viewport = viewport;

        IReadOnlyList<DiagramElement> after;
        try
        {
            after = Visible();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that vanished or locked between two pans costs this answer, never the
            // session; the reported viewport is kept, so the next report or change catches up.
            _logger.Warning(exception, "Could not re-read {BodyPath} for a view report", _bodyPath);
            return [];
        }

        _delivered = after;

        // Add for what appeared, then Remove for what left. That order is the one the reference
        // implementations use; the requirements anticipated the opposite and the code wins.
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

        return deltas;
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

    /// <summary>
    /// The map as the elements this connection reported viewport can see. Used by every path that
    /// decides what to send, so a document change cannot re-deliver what a viewport culled.
    /// </summary>
    private IReadOnlyList<DiagramElement> Visible()
    {
        var map = WardleyParser.Parse(_documents.GetOrLoad(_bodyPath));
        return _mapper.Visible(map, _documents.Identities(_bodyPath), _viewport);
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
            var current = Visible();
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
