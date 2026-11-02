using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The Structurizr DSL side of the core <see cref="LineDocument"/>: the lines with their 1-based
/// numbers, which is what the parsers read and what a line in the model points at, and the edits
/// the commands make, by those numbers.
/// </summary>
/// <remarks>
/// C4 held a line document of its own until code-structure T128, and moved onto the core one
/// with these four as its whole remainder. Where the two disagreed about line endings the core's
/// behaviour won (behaviour changes B-05 and B-16). Since c4-disl-fbl task 15 only <see cref="AddC4ViewCommand"/>
/// edits by line, so the three it uses are what is left; replacing a line went with the commands that did it.
/// </remarks>
public static class C4Lines
{
    /// <summary>The lines paired with their 1-based numbers.</summary>
    public static IEnumerable<C4Line> CodeLines(this LineDocument document) =>
        document.Lines.Select((line, index) => new C4Line(line.Text, (uint)(index + 1)));

    /// <summary>Inserts <paramref name="text"/> so that it becomes line <paramref name="number"/>.</summary>
    public static void InsertLine(this LineDocument document, uint number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        document.Insert((int)number - 1, [text]);
    }

    /// <summary>Removes lines <paramref name="from"/> to <paramref name="to"/> inclusive, 1-based.</summary>
    public static void RemoveLines(this LineDocument document, uint from, uint to) =>
        document.Remove(new LineRange((int)from - 1, (int)to - 1));
}
