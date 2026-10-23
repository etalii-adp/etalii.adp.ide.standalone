using System.Globalization;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// The derived ids the Hierarchical Requirement Designer's fixtures name elements by, standing in for DESL as
/// <see cref="NaturalIds"/> stands in for DISL. Each mirrors <c>persistence.ids</c> of etalii-adp/etalii.adp
/// <c>definitions/designers/hierarchical-requirement.des</c>: a tag, a category and a state by their name in lower case,
/// a stored filter by its name, and an assessment by its zero-based place among the document's assessments.
/// </summary>
public static class DerivedIds
{
    public static string Tag(string name) => "tag/" + LowerAscii(name);

    public static string Category(string name) => "category/" + LowerAscii(name);

    public static string State(string name) => "state/" + LowerAscii(name);

    public static string Filter(string name) => "filter/" + name;

    public static string Assessment(int position) => string.Create(CultureInfo.InvariantCulture, $"assessment/{position}");

    /// <summary>CEL's <c>lowerAscii</c>: the ASCII capitals lowered, every other character kept.</summary>
    private static string LowerAscii(string text) => string.Concat(text.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c));
}
