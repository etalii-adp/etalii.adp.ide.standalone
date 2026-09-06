using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using Serilog;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// One open Databricks diagram for one connection. Core owns the stream, the authorization and
/// the connection lifetime; this owns which of the family's three readings the diagram shows
/// and expresses every change as a <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <para>
/// It filters to what the reader can see: <see cref="UpdateView"/> renders against the last
/// reported viewport and answers with the difference from what this connection already holds, so
/// panning and zooming bring content in rather than merely moving over a set fixed at open
/// (view-delta-adoption Requirement 1.1). This class used to say the opposite - that a bounded
/// diagram of tens of elements has nothing to virtualize - and that was a fair local judgement
/// while the documents stayed small. It is superseded: the loop belongs everywhere, and a module
/// that declines it stays correct only for as long as nobody writes a large bundle.
/// </para>
/// <para>
/// A connection that never reports keeps exactly the old behaviour, because the viewport starts
/// <see cref="DiagramViewport.Unbounded"/> and everything intersects it.
/// </para>
/// <para>
/// A drag never touches the body file: repositioning dispatches the core
/// <see cref="SetRegistrationLayoutCommand"/>, which writes the <c>.adp</c>'s <c>layout:</c>
/// block - the layout-in-.adp rule this family exists to exercise (Requirement 7). The change
/// comes back through the registration's reload, and every session on the file re-renders with
/// the authored position overlaid.
/// </para>
/// </remarks>
public sealed class DatabricksSession : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<DatabricksSession>();

    private readonly string _bodyPath;
    private readonly string? _registrationPath;
    private readonly DiagramOrigin _origin;
    private readonly string? _resourceKey;
    private readonly IDatabricksDocumentStore _documents;
    private readonly DatabricksElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the diagram read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>What this connection was last sent, so a change can be diffed against it.</summary>
    private IReadOnlyList<DiagramElement> _delivered = [];

    /// <summary>
    /// The last viewport this connection reported. Unbounded until it reports one, so a diagram
    /// opens whole and a client that never reports keeps the behaviour it had before this
    /// session filtered anything (view-delta-adoption Requirement 1.4).
    /// </summary>
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public DatabricksSession(
        string bodyPath,
        string? registrationPath,
        DiagramOrigin origin,
        string? resourceKey,
        IDatabricksDocumentStore documents,
        DatabricksElementMapper mapper,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _bodyPath = bodyPath;
        _registrationPath = registrationPath;
        _origin = origin;
        _resourceKey = resourceKey;
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
        var elements = Visible();
        _delivered = elements;

        return elements.Count > 0
            ? [new DiagramAddDelta(elements)]
            : [];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The whole loop, in four lines: remember what the reader can see, render what falls inside
    /// it, and answer with the difference from what this connection already holds. A pan that
    /// brings nothing new into view diffs to nothing and answers with an empty list, which is the
    /// same shape this returned before it filtered anything - and the reason a reader who is not
    /// moving costs the backend nothing.
    /// </remarks>
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _viewport = viewport;

        var after = Visible();
        var deltas = _mapper.Diff(_delivered, after);
        _delivered = after;

        return deltas;
    }

    /// <summary>Refused: nothing in these diagrams nests under a parent - their place is a position.</summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("A Databricks element has no parent to move it under; dragging changes where it sits on the canvas.");
    }

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - and never writes the body file (Requirement 7.6/7.7).
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
            return "This diagram was opened without a registration, so there is nowhere to store a position.";
        }

        if (elementId.StartsWith("edge:", StringComparison.Ordinal)
            || elementId.StartsWith("override:", StringComparison.Ordinal)
            || elementId.StartsWith("flow:", StringComparison.Ordinal))
        {
            // An edge has no position of its own - it follows its endpoints.
            return "That element is not something this diagram can move.";
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

    /// <summary>What this connection should currently hold: the full rendering, culled to the
    /// viewport it last reported.</summary>
    private IReadOnlyList<DiagramElement> Visible() => _mapper.Visible(Render(), _viewport);

    private IReadOnlyList<DiagramElement> Render()
    {
        var entry = _documents.GetOrLoad(_bodyPath);
        var stored = _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        switch (_origin.Type)
        {
            case "job":
            {
                var job = Pick(entry.Jobs, job => job.Key);
                if (job is null)
                {
                    return [];
                }

                var positions = RegistrationLayout.Apply(DatabricksJobLayout.Positions(job), stored);
                return _mapper.JobElements(job, positions);
            }

            case "pipeline":
            {
                var pipeline = Pick(entry.Pipelines, pipeline => pipeline.Key);
                if (pipeline is null)
                {
                    return [];
                }

                var positions = RegistrationLayout.Apply(DatabricksPipelineLayout.Positions(pipeline), stored);
                return _mapper.PipelineElements(pipeline, positions);
            }

            default:
            {
                var positions = RegistrationLayout.Apply(DatabricksBundleLayout.Positions(entry.Bundle), stored);
                return _mapper.BundleElements(entry.Bundle, positions);
            }
        }
    }

    /// <summary>
    /// The declaration this session shows: the one the <c>resource:</c> header names, or the
    /// file's first. A key that names nothing yields the empty diagram rather than a crash -
    /// the header may outlive a rename made outside ADP.
    /// </summary>
    private T? Pick<T>(IReadOnlyList<T> candidates, Func<T, string> key)
        where T : class =>
        _resourceKey is { Length: > 0 }
            ? candidates.FirstOrDefault(candidate => key(candidate) == _resourceKey)
            : candidates.FirstOrDefault();

    private void OnDocumentChanged(object? sender, DatabricksDocumentChangedEventArgs args)
    {
        if (!string.Equals(args.Path, _bodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            // Filtered through the same viewport the reader last reported: an edit outside what
            // they can see must not arrive as an add for a box they would then have to be sent a
            // remove for.
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
