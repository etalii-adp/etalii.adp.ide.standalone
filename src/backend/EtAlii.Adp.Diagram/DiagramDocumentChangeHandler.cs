using Serilog;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// How a diagram session reacts to its document changing - written once, where each session used
/// to write its own (backend-centralization R5).
/// </summary>
/// <remarks>
/// <para>
/// <b>The one shape</b> (R5.1): ignore a change to any other path, render, diff against what this
/// connection was last given (<see cref="DiagramDiff"/>), raise the deltas if there are any, and on
/// a read failure log a warning naming the path without raising.
/// </para>
/// <para>
/// <b>It owns what was delivered</b>, because the baseline, a viewport report and a document change
/// all move it, and a diff is only right against the last of them. A session asks here for its
/// baseline (<see cref="Deliver"/>) and for a viewport answer (<see cref="Refresh"/>) instead of
/// keeping a copy of its own, so there is one record of what the client holds.
/// </para>
/// <para>
/// <b>Only a read failure is caught</b> - <see cref="IOException"/> or
/// <see cref="UnauthorizedAccessException"/>, a file that vanished or locked mid-reload. It costs
/// this notification, never the session, and the next change tries again. Anything else propagates
/// to the caller: the reload bridge already guards each document's reload separately, so an
/// unexpected failure costs that document's reload and is logged there with its path, rather than
/// being swallowed here under a generic message (R5.2).
/// </para>
/// <para>
/// <b>Render, diff, record and raise happen under one lock</b>, because a change arrives on the
/// watcher's thread while a baseline or a viewport report arrives on a request's. Split them and
/// the client can end on a stale state. If one thread renders, a second renders a later state and
/// records it, and the first then records its older rendering, the client's next diff is measured
/// from the wrong place. And if two raises swap order, an element both changed ends at the older
/// of its two states, because an add is an upsert. The copies this replaces rendered and raised
/// outside any lock and had both races. Holding the lock across the raise is safe only because the
/// raise does not block: <c>DiagramService</c> writes it to an unbounded channel. A raise that could
/// block would hold up every baseline and viewport report for that session.
/// </para>
/// </remarks>
public sealed class DiagramDocumentChangeHandler
{
    private static readonly ILogger _logger = Log.ForContext<DiagramDocumentChangeHandler>();

    private readonly string _bodyPath;
    private readonly Func<IReadOnlyList<DiagramElement>> _render;
    private readonly Action<IReadOnlyList<DiagramDelta>> _raise;
    private readonly Lock _gate = new();
    private IReadOnlyList<DiagramElement> _delivered = [];

    /// <param name="bodyPath">The document this session shows; a change to any other path is ignored.</param>
    /// <param name="render">What the connection should hold now - usually filtered through its viewport.</param>
    /// <param name="raise">Called with the deltas when a change produced any, never with none.</param>
    public DiagramDocumentChangeHandler(
        string bodyPath,
        Func<IReadOnlyList<DiagramElement>> render,
        Action<IReadOnlyList<DiagramDelta>> raise)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(raise);

        _bodyPath = bodyPath;
        _render = render;
        _raise = raise;
    }

    /// <summary>What the connection was last given.</summary>
    public IReadOnlyList<DiagramElement> Delivered
    {
        get
        {
            lock (_gate)
            {
                return _delivered;
            }
        }
    }

    /// <summary>
    /// Renders, and records the result as what the connection now holds - a session's baseline.
    /// A read failure propagates: a baseline that cannot be read is the caller's to report.
    /// </summary>
    public IReadOnlyList<DiagramElement> Deliver()
    {
        lock (_gate)
        {
            var elements = _render();
            _delivered = elements;
            return elements;
        }
    }

    /// <summary>
    /// Renders, and returns the deltas from what the connection held to that, recording it - a
    /// session's answer to a viewport report, which it returns rather than raises. A read failure
    /// propagates, leaving the record as it was.
    /// </summary>
    public IReadOnlyList<DiagramDelta> Refresh()
    {
        lock (_gate)
        {
            var elements = _render();
            var deltas = DiagramDiff.Between(_delivered, elements);
            _delivered = elements;
            return deltas;
        }
    }

    /// <summary>
    /// The session's reaction to <paramref name="path"/> changing (R5.1). Pass the path from the
    /// store's change event as it is: the comparison ignores case, as every session's did.
    /// </summary>
    public void OnDocumentChanged(string path)
    {
        if (!string.Equals(path, _bodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_gate)
        {
            IReadOnlyList<DiagramDelta> deltas;
            try
            {
                deltas = Refresh();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file that vanished or locked mid-reload costs this notification, never the
                // session: the next change tries again.
                _logger.Warning(exception, "Could not re-read {BodyPath} after a change", _bodyPath);
                return;
            }

            if (deltas.Count > 0)
            {
                _raise(deltas);
            }
        }
    }
}
