using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The attachments a finished connect gesture carries, on top of the shared <c>rel:</c> grammar.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shared grammar has two ends and nothing else</b>, and the context channel carries one
/// element id per call with no field for a position. An influence is not complete without the phase,
/// edge and fraction at each end (Requirement 6.2, 6.3), and asking for them in a second command
/// would make drawing one influence two undo steps. So each END may carry its attachment as a
/// suffix: <c>{trendId}@{phase}/{edge}/{at}</c>, for example
/// <c>rel:steam-engine@plateau/bottom/0.3-&gt;railways@peak/top/0.1</c>.
/// </para>
/// <para>
/// <b>This composes on <see cref="GestureIds"/> rather than copying it</b>: the shared parser splits
/// the id at its first arrow as always, and only then is each end split at its LAST <c>@</c>. An end
/// whose suffix does not read as an attachment is taken whole as a trend id, so a hand-authored id
/// containing <c>@</c> still works, and a plain <c>rel:a-&gt;b</c> is accepted with default ends.
/// </para>
/// </remarks>
public static class GhgGestures
{
    /// <summary>What separates a trend id from the attachment its gesture end carries.</summary>
    public const char AttachmentSeparator = '@';

    /// <summary>A relation id carrying both attachments.</summary>
    public static string Relation(string from, GhgEnd fromEnd, string to, GhgEnd toEnd)
    {
        ArgumentNullException.ThrowIfNull(fromEnd);
        ArgumentNullException.ThrowIfNull(toEnd);
        return GestureIds.Relation($"{from}{AttachmentSeparator}{fromEnd}", $"{to}{AttachmentSeparator}{toEnd}");
    }

    /// <summary>Parses a relation id, and the attachment each end carries when it carries one.</summary>
    public static bool TryParseRelation(string? elementId, out string from, out GhgEnd? fromEnd, out string to, out GhgEnd? toEnd)
    {
        fromEnd = null;
        toEnd = null;
        if (!GestureIds.TryParseRelation(elementId, out from, out to))
        {
            return false;
        }

        (from, fromEnd) = Split(from);
        (to, toEnd) = Split(to);
        return from.Length > 0 && to.Length > 0;
    }

    private static (string Id, GhgEnd? End) Split(string gestureEnd)
    {
        var at = gestureEnd.LastIndexOf(AttachmentSeparator);
        return at > 0 && GhgEnd.TryParse(gestureEnd[(at + 1)..], out var end)
            ? (gestureEnd[..at], end)
            : (gestureEnd, null);
    }
}
