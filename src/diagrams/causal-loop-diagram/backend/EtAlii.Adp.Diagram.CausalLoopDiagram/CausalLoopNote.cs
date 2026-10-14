using System.Text;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The note of a link the binding has read, read again as the tool has always read it: the last word
/// after the arrow that is not a polarity, <c>delayed</c>, <c>flipped</c> or a weight, quoted or not
/// (causal-loop-disl-fbl Q2, findings B1 and B12) - and written back where the tool read it from.
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands in for a language gap</b> (the definition's companion, gap 2). FBL's regular expressions
/// have no lookahead or not-followed-by, so the binding cannot tell a bare-word note from a flag, and a
/// <c>word</c> slot takes the first matching word where the tool takes the last; the binding therefore
/// reads only a quoted note, the first of two. Whether a line is a link, and every other attribute of
/// one, is the binding's alone: this class is asked only about a statement the binding read as a link,
/// and only about its note.
/// </para>
/// <para>
/// <b>Writing follows reading.</b> Where the note the tool reads is the binding's own - the one quoted
/// word that is nothing else - the library writes it. Where it is not, a bare word or the last of two,
/// the library would write the binding's slot and the tool would read the old word again; so the new
/// note replaces the word the tool read, and an empty one removes every word the tool could read as
/// one, each with the whitespace before it. That splice is here, beside the reading it follows, so that
/// dropping this stand-in drops it too.
/// </para>
/// <para>
/// It goes when FBL can state either, or when the owner rules the binding's reading is the tool's.
/// </para>
/// </remarks>
public static class CausalLoopNote
{
    private const string Link = "Link";

    private const string Note = "note";

    /// <summary>The attribute of a link that holds its note.</summary>
    public const string Attribute = Note;

    /// <summary><paramref name="model"/> with the note of every link read from <paramref name="bytes"/> as the tool reads it.</summary>
    public static FblModel Applied(FblModel model, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bytes);
        return model with
        {
            Elements = [.. model.Elements.Select(element => element.Type == Link ? WithNote(element, StatementOf(element, bytes)) : element)],
        };
    }

    /// <summary>The note of the link <paramref name="id"/> as the tool reads it, empty for none; null where <paramref name="id"/> is no link.</summary>
    /// <param name="model">What the binding itself reads from <paramref name="bytes"/>, its notes not replaced.</param>
    public static string? Read(FblModel model, byte[] bytes, string id)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bytes);
        return model.Find(id) is { Type: Link } link && Candidates(StatementOf(link, bytes)) is var candidates
            ? candidates is [.., var last] ? last.Text : ""
            : null;
    }

    /// <summary>
    /// The splices that write the note <paramref name="set"/> gives a link whose note the tool reads where
    /// the binding does not - none when it is the note read already - and the rest of the change for the
    /// library; null where the library writes the note itself, or <paramref name="set"/> sets none.
    /// </summary>
    /// <param name="model">What the binding itself reads from <paramref name="bytes"/>, its notes not replaced.</param>
    public static (Edit Edit, ModelChange.Set Remaining)? Splices(FblModel model, byte[] bytes, ModelChange.Set set)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(set);
        if (!set.Attributes.TryGetValue(Note, out var value) || model.Find(set.Id) is not { Type: Link } link) return null;

        var statement = StatementOf(link, bytes);
        var candidates = Candidates(statement);
        // None: the library inserts the note. One quoted word that is nothing else: the binding's own slot.
        if (candidates is [] or [{ Quoted: true }]) return null;

        var note = value as string ?? "";
        var rest = set.Attributes.Where(attribute => attribute.Key != Note).ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
        // The note the tool reads already: nothing to write, so nothing is written.
        if (note.Length > 0 && candidates[^1].Text == note) return (new Edit([]), set with { Attributes = rest });

        var splices = note.Length == 0
            ? candidates.Select(word => new Splice(SpliceOperation.RemoveKey, Offset(link, statement, WhitespaceBefore(statement, word.Start)), Offset(link, statement, word.End), ""))
            : [new Splice(SpliceOperation.ReplaceValue, Offset(link, statement, candidates[^1].Start), Offset(link, statement, candidates[^1].End), $"\"{note}\"")];
        return (new Edit([.. splices]), set with { Attributes = rest });
    }

    private static string StatementOf(FblElement element, byte[] bytes) =>
        Encoding.UTF8.GetString(bytes, element.OwnSpan.Start, element.OwnSpan.End - element.OwnSpan.Start);

    private static FblElement WithNote(FblElement element, string statement)
    {
        var attributes = new Dictionary<string, object?>(element.Attributes, StringComparer.Ordinal);
        if (Candidates(statement) is [.., { Text.Length: > 0 } last]) attributes[Note] = last.Text;
        else attributes.Remove(Note);
        return element with { Attributes = attributes };
    }

    /// <summary>
    /// The words after a link's arrow that the tool reads as a note, in order, the last one winning: each
    /// with its text, without quotes, and where it is in <paramref name="statement"/>, quotes included.
    /// </summary>
    private static List<Word> Candidates(string statement) =>
    [
        .. Words(statement).Skip(4).Where(word => !(word.Text is "delayed" or "flipped" or "+" or "s" or "S" or "-" or "o" or "O" || word.Text.StartsWith("weight=", StringComparison.Ordinal))),
    ];

    /// <summary>
    /// The statement's words, as the tool has always split it: at whitespace, a double-quoted run kept
    /// together as one word without its quotes, and an unclosed quote running to the end of the line.
    /// </summary>
    private static List<Word> Words(string text)
    {
        var words = new List<Word>();
        var index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            if (text[index] == '"')
            {
                var close = text.IndexOf('"', index + 1);
                if (close < 0)
                {
                    words.Add(new Word(text[(index + 1)..], index, text.TrimEnd().Length, true));
                    break;
                }

                words.Add(new Word(text[(index + 1)..close], index, close + 1, true));
                index = close + 1;
                continue;
            }

            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            words.Add(new Word(text[start..index], start, index, false));
        }

        return words;
    }

    private static int WhitespaceBefore(string statement, int start)
    {
        while (start > 0 && statement[start - 1] is ' ' or '\t')
        {
            start--;
        }

        return start;
    }

    /// <summary>The body offset of character <paramref name="index"/> of <paramref name="statement"/>, the text of <paramref name="link"/>.</summary>
    private static int Offset(FblElement link, string statement, int index) =>
        link.OwnSpan.Start + Encoding.UTF8.GetByteCount(statement.AsSpan(0, index));

    /// <param name="Text">The word, without its quotes.</param>
    /// <param name="Start">Where it starts in the statement, at its opening quote for a quoted one.</param>
    /// <param name="End">Where it ends, after its closing quote.</param>
    /// <param name="Quoted">Whether it is written between double quotes.</param>
    private sealed record Word(string Text, int Start, int End, bool Quoted);
}
