using System.Collections.Concurrent;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Which element a connection gesture is currently drawn from, per connection and document -
/// the state Requirement 11.6 puts the canvas into when Connect is chosen from the menu.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the context-action channel can carry <b>one element per call</b> (the
/// source), and a connection needs two. Executing Connect on the first element arms this;
/// executing it on the second completes the pair and dispatches the command. The canvas mirrors
/// the armed state locally to draw the pending curve, and <c>Escape</c> simply never sends the
/// second call.
/// </para>
/// <para>
/// View state, not document state: nothing here is written to a file, and one connection's
/// half-drawn gesture is invisible to every other. Stale entries are harmless - the next arm
/// replaces them - so nothing expires them.
/// </para>
/// </remarks>
public sealed class TimelineConnectState
{
    private readonly ConcurrentDictionary<(ShortGuid WatchId, string BodyPath), string> _pending = new();

    /// <summary>
    /// Arms the gesture from <paramref name="elementId"/>, or - when it was already armed from
    /// another element - completes it and returns the pair. Arming from the element already
    /// armed cancels, because connecting a thing to itself is refused anyway and a second tap
    /// on the same element reads as "never mind".
    /// </summary>
    public (string From, string To)? ArmOrComplete(ShortGuid watchId, string bodyPath, string elementId)
    {
        var key = (watchId, bodyPath);
        if (_pending.TryRemove(key, out var from))
        {
            if (string.Equals(from, elementId, StringComparison.Ordinal))
            {
                return null;
            }

            return (from, elementId);
        }

        _pending[key] = elementId;
        return null;
    }

    /// <summary>Whether a gesture is armed for this connection and document, and from what.</summary>
    public string? PendingFor(ShortGuid watchId, string bodyPath) =>
        _pending.TryGetValue((watchId, bodyPath), out var from) ? from : null;

    /// <summary>Drops an armed gesture without completing it - the canvas's Escape.</summary>
    public void Clear(ShortGuid watchId, string bodyPath) =>
        _pending.TryRemove((watchId, bodyPath), out _);
}
