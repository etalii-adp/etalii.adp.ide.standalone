using System.Text;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The note of a link the binding has read, read again as the tool has always read it: the last word
/// after the arrow that is not a polarity, <c>delayed</c>, <c>flipped</c> or a weight, quoted or not
/// (causal-loop-disl-fbl Q2, findings B1 and B12).
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands in for a language gap</b> (the definition's companion, gap 2). FBL's regular expressions
/// have no lookahead or not-followed-by, so the binding cannot tell a bare-word note from a flag, and a
/// <c>word</c> slot takes the first matching word where the tool takes the last; the binding therefore
/// reads only a quoted note, the first of two. Whether a line is a link, and every other attribute of
/// one, is the binding's alone: this class is asked only about a statement the binding read as a link,
/// and only replaces its note.
/// </para>
/// <para>
/// It goes when FBL can state either, or when the owner rules the binding's reading is the tool's.
/// </para>
/// </remarks>
public static class CausalLoopNote
{
    private const string Link = "Link";

    private const string Note = "note";

    /// <summary><paramref name="model"/> with the note of every link read from <paramref name="bytes"/> as the tool reads it.</summary>
    public static FblModel Applied(FblModel model, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bytes);
        return model with
        {
            Elements = [.. model.Elements.Select(element => element.Type == Link ? WithNote(element, Encoding.UTF8.GetString(bytes, element.OwnSpan.Start, element.OwnSpan.End - element.OwnSpan.Start)) : element)],
        };
    }

    /// <summary>The note of the link statement <paramref name="statement"/>; empty for none.</summary>
    public static string Of(string statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var note = "";
        foreach (var word in CausalLoopUnreadable.Words(statement).Skip(4))
        {
            if (word is "delayed" or "flipped" or "+" or "s" or "S" or "-" or "o" or "O" || word.StartsWith("weight=", StringComparison.Ordinal)) continue;
            note = word;
        }

        return note;
    }

    private static FblElement WithNote(FblElement element, string statement)
    {
        var attributes = new Dictionary<string, object?>(element.Attributes, StringComparer.Ordinal);
        if (Of(statement) is { Length: > 0 } note) attributes[Note] = note;
        else attributes.Remove(Note);
        return element with { Attributes = attributes };
    }
}
