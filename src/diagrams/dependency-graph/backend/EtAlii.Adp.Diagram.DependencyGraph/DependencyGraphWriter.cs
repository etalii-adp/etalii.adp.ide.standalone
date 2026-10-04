using System.Globalization;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Turns an edit into a splice of the lines that edit affects, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Every write in this module goes through here, and every one of them is a splice of a range the
/// parser recorded. Nothing serialises a model back to a file, which is what makes "only the
/// affected lines change" true by construction rather than by care, and "unmodelled keys survive"
/// free, since a line nobody splices is a line nobody can damage.
/// </para>
/// <para>
/// A write invalidates the model that located it: line numbers after a splice have moved. Callers
/// re-parse rather than adjusting ranges by hand, which is cheap and removes a whole class of
/// off-by-one bug.
/// </para>
/// </remarks>
public static class DependencyGraphWriter
{
    /// <summary>The document's section key for the directed depends-on edges.</summary>
    internal const string RelationsSection = "relations:";

    private const string ElementsSection = "elements:";

    /// <summary>Rewrites an element's label, leaving its quoting style alone elsewhere.</summary>
    public static void SetLabel(LineDocument document, DependencyGraphElement element, string label)
    {
        ArgumentNullException.ThrowIfNull(element);
        LineSplice.SetKey(document, element.Range, "label", LineSplice.Quote(label));
    }

    /// <summary>Rewrites an element's horizontal coordinate.</summary>
    public static void SetX(LineDocument document, DependencyGraphElement element, double x)
    {
        ArgumentNullException.ThrowIfNull(element);
        LineSplice.SetKey(document, element.Range, "x", Number(x));
    }

    /// <summary>Rewrites an element's row.</summary>
    public static void SetRow(LineDocument document, DependencyGraphElement element, int row)
    {
        ArgumentNullException.ThrowIfNull(element);
        LineSplice.SetKey(document, element.Range, "row", row.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Rewrites a relation's label, or removes the key when the label is cleared.</summary>
    public static void SetRelationLabel(LineDocument document, DependencyGraphRelation relation, string label)
    {
        ArgumentNullException.ThrowIfNull(relation);

        if (label.Length == 0)
        {
            LineSplice.RemoveKey(document, relation.Range, "label");
            return;
        }

        LineSplice.SetKey(document, relation.Range, "label", LineSplice.Quote(label));
    }

    /// <summary>
    /// Appends an element to the document, after the last one already there.
    /// </summary>
    /// <remarks>
    /// The indentation is copied from whatever is already in the file rather than imposed, so a
    /// document written with four spaces stays written with four spaces. Only an empty document
    /// falls back to this module's own default.
    /// </remarks>
    public static void InsertElement(
        LineDocument document,
        DependencyGraphModel model,
        string id,
        string label,
        double x,
        int row)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        (string itemIndent, string dashGap, string keyIndent) = LineSplice.IndentOf(document, model.Elements.Select(element => element.Range));
        var lines = new List<string>
        {
            $"{itemIndent}-{dashGap}id: {id}",
            $"{keyIndent}label: {LineSplice.Quote(label)}",
            $"{keyIndent}x: {Number(x)}",
            $"{keyIndent}row: {row.ToString(CultureInfo.InvariantCulture)}",
        };

        document.Insert(
            LineSplice.InsertionPointFor(document, model.Elements.Select(element => element.Range), ElementsSection),
            lines);
    }

    /// <summary>
    /// Appends a relation to the document: <paramref name="from"/> depends on <paramref name="to"/>.
    /// </summary>
    public static void InsertRelation(
        LineDocument document,
        DependencyGraphModel model,
        string id,
        string from,
        string to,
        string label)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        (string itemIndent, string dashGap, string keyIndent) = LineSplice.IndentOf(document, model.Relations.Select(relation => relation.Range));
        var lines = new List<string>
        {
            $"{itemIndent}-{dashGap}id: {id}",
            $"{keyIndent}from: {from}",
            $"{keyIndent}to: {to}",
        };

        if (label.Length > 0)
        {
            lines.Add($"{keyIndent}label: {LineSplice.Quote(label)}");
        }

        var at = LineSplice.InsertionPointFor(document, model.Relations.Select(relation => relation.Range), RelationsSection);
        if (at < 0)
        {
            // No `relations:` key yet, so the section is created at the end of the document
            // along with its first entry.
            document.Insert(document.Lines.Count, [RelationsSection, .. lines]);
            return;
        }

        document.Insert(at, lines);
    }

    /// <summary>
    /// Removes an element and every relation that referenced it, in one splice each, from the
    /// bottom of the document upwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Descending order matters: removing a range shifts every line after it, so removing top-down
    /// would leave every later range pointing at the wrong lines. Sorting once here is cheaper
    /// than recomputing ranges between removals, and considerably harder to get wrong.
    /// </para>
    /// <para>
    /// The relations go with the element because a dependency on something that no longer exists
    /// is not a graph anybody wants; the count is wanted beforehand, which
    /// <see cref="RelationsTouching"/> answers without performing the removal.
    /// </para>
    /// </remarks>
    public static void RemoveElement(
        LineDocument document, DependencyGraphModel model, DependencyGraphElement element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        var ranges = RelationsTouching(model, element.Id)
            .Select(relation => relation.Range)
            .Append(element.Range)
            .OrderByDescending(range => range.Start)
            .ToList();

        foreach (var range in ranges)
        {
            document.Remove(range);
        }
    }

    /// <summary>Removes one relation.</summary>
    public static void RemoveRelation(LineDocument document, DependencyGraphRelation relation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(relation);
        document.Remove(relation.Range);
    }

    /// <summary>Whether the document currently carries a <c>relations:</c> section key.</summary>
    /// <remarks>
    /// An insert creates the section on demand, so a command whose inverse must restore the file
    /// byte for byte asks this first and has its undo remove what the insert created - a stray
    /// <c>relations:</c> header after an undo is the one line that would break identity.
    /// </remarks>
    public static bool HasRelationsSection(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return LineSplice.FindSection(document, RelationsSection) >= 0;
    }

    /// <summary>
    /// Removes a <c>relations:</c> section key left with no entries - the undo of the insert
    /// that created it. A no-op while any relation remains.
    /// </summary>
    public static void RemoveRelationsSectionIfEmpty(LineDocument document, DependencyGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        if (model.Relations.Count > 0)
        {
            return;
        }

        var index = LineSplice.FindSection(document, RelationsSection);
        if (index >= 0)
        {
            document.Remove(new LineRange(index, index));
        }
    }

    /// <summary>
    /// The relations that would go with an element, so an action can say how many before it runs.
    /// </summary>
    public static IReadOnlyList<DependencyGraphRelation> RelationsTouching(DependencyGraphModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.Relations
            .Where(relation =>
                string.Equals(relation.From, elementId, StringComparison.Ordinal) ||
                string.Equals(relation.To, elementId, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// A coordinate as document text: invariant, and in its shortest round-trippable form so a
    /// whole number stays a whole number rather than sprouting a decimal point on every drag.
    /// </summary>
    internal static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
}
