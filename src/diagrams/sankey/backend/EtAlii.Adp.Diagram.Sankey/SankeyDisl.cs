using System.Runtime.CompilerServices;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The DISL model of one reading of a <c>.skv</c> document, built from what <see cref="SankeyParser"/> read, with
/// hand-made <see cref="FblElement"/> records in the shape the module's binding reads them, through
/// <see cref="DislModelBuilder.From(FblModel, DislSpecification)"/> (sankey-disl-fbl task 6).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is an adapter for one step.</b> The menus and the rows are derived from the definition before the body is
/// read through the binding, so that derivation changes and reading does not; reading through the binding (task 10)
/// deletes it.
/// </para>
/// <para>
/// <b>Ids.</b> A node is read with the id it is written with, kept as the host attribute <c>storedId</c> as the
/// binding keeps it. A node without an id, and a later holder of an id another node already holds, is read with an
/// ephemeral id no written id can equal, and keeps what it is written with in <c>storedId</c>: so the first holder
/// alone is found by its id, as <see cref="SankeyModel.NodeOf"/> finds it. A flow's id is the definition's derived one,
/// its stated id or its ends as written.
/// </para>
/// <para>
/// <b>A flow end</b> is the text it is written with, which names the first node written with it; one that names no
/// node stays unset in the model and is kept as written, for <c>writtenEnd</c>.
/// </para>
/// <para>
/// <b>Once per reading</b>: the model is kept with the <see cref="SankeyModel"/> it was built from, which is replaced
/// on every change, so a menu and a row read one snapshot. Read it, never change it.
/// </para>
/// </remarks>
internal static class SankeyDisl
{
    /// <summary>What no written id can begin with: the start of an ephemeral id.</summary>
    private const char Ephemeral = '\u0001';

    private static readonly ConditionalWeakTable<SankeyModel, DislModel> _models = new();

    /// <summary>The model of <paramref name="model"/> under the bundled definition, built once per reading.</summary>
    public static DislModel Of(SankeyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _models.GetValue(model, read => DislModelBuilder.From(new FblModel(Elements(read), [], false), SankeyDefinition.Specification));
    }

    private static List<FblElement> Elements(SankeyModel model)
    {
        var settings = model.Settings;
        var elements = new List<FblElement>
        {
            Element("diagram", "Document", 0, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["format"] = settings.FormatLine >= 0 ? settings.Format : null,
                ["flowColor"] = settings.FlowColorLine >= 0 ? settings.FlowColor : null,
                ["thickness"] = settings.ThicknessLine >= 0 ? settings.Thickness : null,
            }),
        };

        var holders = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < model.Nodes.Count; index++)
        {
            var node = model.Nodes[index];

            // The first holder of an id is read with it; a node without one, or a later holder, with an id no written one can equal.
            var id = node.Id.Length > 0 && holders.Add(node.Id) ? node.Id : $"{Ephemeral}{index}{Ephemeral}{node.Id}";
            elements.Add(Element(id, "Node", Line(node.Range), new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["storedId"] = Present(node.Id),
                ["name"] = Present(node.Name),
                ["description"] = Present(node.Description),
                ["color"] = Present(node.Color),
                ["column"] = node.Column is { } column ? (long)column : null,
                ["note"] = Present(node.Note),
                ["format"] = Present(node.Format),
            }));
        }

        elements.AddRange(model.Flows.Select(flow => Element(flow.Id, "Flow", Line(flow.Range), new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["statedId"] = Present(flow.ExplicitId),
            ["from"] = Present(flow.From),
            ["to"] = Present(flow.To),
            ["value"] = flow.Value,
            ["step"] = flow.Step,
            ["color"] = Present(flow.Color),
            ["description"] = Present(flow.Description),
        })));
        return elements;
    }

    /// <summary>A value the parser read as empty text is not written: absent, as the binding reads a key the entry does not state.</summary>
    private static string? Present(string value) => value.Length > 0 ? value : null;

    /// <summary>The line an entry is written at, 1-based, as a finding reports it.</summary>
    private static int Line(Documents.LineRange lines) => lines.Start + 1;

    /// <summary>An element of the binding's <paramref name="type"/>, with the attributes the entry states; a flow is an element with reference attributes, as the binding reads it.</summary>
    private static FblElement Element(string id, string type, int line, Dictionary<string, object?> attributes) =>
        new(
            id,
            type == "Node" && id[0] != Ephemeral,
            type,
            type,
            false,
            attributes.Where(attribute => attribute.Value is not null).ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal),
            null,
            null,
            null,
            null,
            default,
            line);
}
