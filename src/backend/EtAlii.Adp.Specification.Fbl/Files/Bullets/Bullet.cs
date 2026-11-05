using EtAlii.Adp.Specification.Fbl.Lines;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Bullets;

/// <summary>
/// A list item of the <c>bullets</c> family (FBL 0.7 §4.8). As a <see cref="Statement"/> its content is
/// the item's text, which a rule with <c>within</c> matches; its own span runs from its marker to the end
/// of its last line, and its line span adds the indentation before the marker and the last line's ending.
/// </summary>
internal sealed class Bullet : Statement
{
    private readonly List<int> _blanks = [];
    private bool _nested;

    /// <summary>The column of the marker, a tab counting four columns.</summary>
    public required int MarkerColumn { get; init; }

    /// <summary>The column where the item's text begins, or the marker's column plus two when it has none.</summary>
    public required int ContentColumn { get; init; }

    /// <summary>The marker: <c>-</c>, <c>*</c> or <c>+</c>.</summary>
    public required char Marker { get; init; }

    /// <summary>The whitespace before the marker, as written.</summary>
    public required string IndentText { get; init; }

    /// <summary>The 0-based lines of the continuation: from its first line to its last before the first nested item, blank lines between them included.</summary>
    public List<int> Continuation { get; } = [];

    /// <summary>Which of <see cref="Continuation"/> are blank lines, read as empty lines.</summary>
    private HashSet<int> BlankLines { get; } = [];

    /// <summary>The nested items, in document order.</summary>
    private IEnumerable<Bullet> Nested => Children.OfType<Bullet>();

    /// <summary>The 0-based line its spans end on: its last continuation line or its last nested item's, whichever is later.</summary>
    private int LastLine { get; set; }

    /// <summary>A line that is no item and stands while this item is the deepest open one.</summary>
    public void AddLine(int line)
    {
        if (!_nested)
        {
            Continuation.AddRange(_blanks);
            Continuation.Add(line);
            _blanks.Clear();
        }
        LastLine = line;
    }

    /// <summary>A blank line while this item is the deepest open one: part of the continuation only when a line follows it there.</summary>
    public void AddBlank(int line)
    {
        if (_nested || Continuation.Count == 0) return;
        _blanks.Add(line);
        BlankLines.Add(line);
    }

    public void Nest(Bullet item)
    {
        _nested = true;
        Children.Add(item);
    }

    /// <summary>Ends the spans at the item's last line, once every line is read; nested items are closed first.</summary>
    public void Close(BodyText text)
    {
        LastLine = Math.Max(Math.Max(LastLine, FirstLine), Nested.Select(item => item.LastLine).DefaultIfEmpty(FirstLine).Max());
        var last = text.Lines[LastLine];
        Own = Own with { End = last.ContentEnd };
        LineSpan = LineSpan!.Value with { End = last.End };
    }

    /// <summary>The span the continuation's value is written in (FBL 0.7 §6.3): from the end of the item line, before its ending, to the end of the continuation's last line, before its ending; null without continuation.</summary>
    public Span? ContinuationSpan(BodyText text) =>
        Continuation.Count == 0 ? null : new Span(text.Lines[FirstLine].ContentEnd, text.Lines[Continuation[^1]].ContentEnd);

    /// <summary>The continuation as text (FBL 0.7 §4.8): its lines joined by LF, each without the whitespace before the content column, a blank line read as an empty one.</summary>
    public string ContinuationText(BodyText text) =>
        string.Join('\n', Continuation.Select(line => BlankLines.Contains(line) ? "" : Columns.Without(text.Text(text.Lines[line].Start, text.Lines[line].ContentEnd), ContentColumn)));
}
