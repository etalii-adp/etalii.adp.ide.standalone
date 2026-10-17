using System.Globalization;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// The binding's reading in the order the tool reads a <c>.dgr</c>, which no binding can state. It
/// stands in for a language gap (<c>definition/dependency-graph.md</c>, section 9) and goes when FBL
/// answers it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nodes are read before dependencies, whatever the file's order (gap 3, finding B5).</b> FBL reads
/// in document order, so a dependency written above the nodes would take an id a node also holds, and
/// the node be reported as the later holder. Here every node comes first: a node takes its stored id
/// before any dependency, a later holder is addressed by its place as FBL addresses it, and each
/// element's <see cref="FblElement.Line"/> is its place in that order, so the runtime reads "first" and
/// "later" as the tool does. <see cref="LineOf"/> gives the file line back.
/// </para>
/// <para>
/// <b>A coordinate or a row that will not read is 0 (gap 2, finding B2, Q4 "as today") needs nothing
/// here.</b> The binding reads <c>x</c> and <c>row</c> as their text, and the DISL model types each value
/// by its attribute with .NET's invariant rules, leaving one that does not fit unset so that it reads as
/// its default, 0 (<c>DislValues.TryFromRead</c>), which is how the tool has always read them. FBL's
/// <c>fallback</c> (FBL 0.5 §5.2) would keep the entry readable too, but it converts by the YAML core
/// schema first, so <c>0x10</c> would be 16 and <c>.inf</c> infinity.
/// </para>
/// </remarks>
internal sealed record DependencyGraphReading(FblModel Model, IReadOnlyList<int> Lines)
{
    /// <summary>The tool's reading of <paramref name="reading"/>.</summary>
    public static DependencyGraphReading Of(FblModel reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (reading.Unreadable) return new DependencyGraphReading(reading, []);

        var ordered = reading.Elements.Where(element => element.Type == "Node")
            .Concat(reading.Elements.Where(element => element.Type == "Dependency"))
            .Concat(reading.Elements.Where(element => element.Type is not "Node" and not "Dependency"))
            .ToList();

        var taken = new HashSet<string>(StringComparer.Ordinal);
        var elements = new List<FblElement>(ordered.Count);
        var lines = new List<int>(ordered.Count);
        foreach (var element in ordered)
        {
            var written = TextOf(element, "storedId");
            var stored = written.Length > 0 && taken.Add(written);
            var id = stored ? written : $"{element.Rule}@{element.Line}";
            taken.Add(id);

            lines.Add(element.Line);
            elements.Add(element with
            {
                Id = id,
                IdIsStored = stored,
                Line = lines.Count,
            });
        }

        return new DependencyGraphReading(reading with { Elements = elements }, lines);
    }

    /// <summary>The 1-based file line of the element whose place in the reading is <paramref name="place"/>.</summary>
    public int LineOf(int place) => Lines[place - 1];

    /// <summary>The text an attribute was read as: its written text, or the empty text when it was not written.</summary>
    internal static string TextOf(FblElement element, string attribute) => element.Attributes.GetValueOrDefault(attribute) switch
    {
        string text => text,
        long or double or bool => Convert.ToString(element.Attributes[attribute], CultureInfo.InvariantCulture) ?? "",
        _ => "",
    };
}
