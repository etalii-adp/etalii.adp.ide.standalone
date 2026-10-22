using System.Globalization;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// What the binding read from a <c>.fdg</c> body, put in the shape the DISL model is built from: the
/// elements before the connections, each with the model id, the ends and the numbers the tool has always
/// given it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which entries are elements and connections, of which type, and what each value is as written, is the
/// binding's</b> (<c>functional-decomposition-graph.fbl</c>). What follows is the tool's, and no binding can
/// state it.
/// </para>
/// <para>
/// <b>A number is converted by the tool's rule</b> (finding B2 of functional-decomposition-graph-disl-fbl):
/// the binding reads <c>x</c>, <c>y</c>, <c>width</c> and <c>height</c> as written, and here they are read
/// as .NET reads a floating-point number in the invariant culture, so <c>"120"</c> is 120, <c>1e2</c> is 100,
/// and <c>0x10</c>, a value that is not a scalar and one that is not there are zero.
/// <see cref="FdgReaderSentences"/> reports the ones that do not read.
/// </para>
/// <para>
/// <b>Model ids are unique; written ids need not be.</b> The first entry with a written id keeps it,
/// elements before connections, and a later one, or one written without an id, gets an ephemeral id no
/// entry is written with. The written id travels as the host attribute <see cref="StoredId"/>, which
/// <see cref="FdgDefinition.ElementOf"/> finds an element by.
/// </para>
/// <para>
/// <b>A connection's end resolves to the first element written with that id</b>, never to a connection;
/// an end that names no element is kept as written, unless it names a connection, which it then cannot be
/// told apart from.
/// </para>
/// <para>
/// <b>A body whose mapping repeats a key is read as a body that could not be read</b> (finding B9): the
/// tool has never read one, and FBL binds the first and reports <c>fbl.duplicate-key</c>.
/// </para>
/// </remarks>
internal static class FdgReading
{
    /// <summary>The host attribute holding the id an entry is written with, empty for none.</summary>
    public const string StoredId = "storedId";

    /// <summary>The binding type of each element type, by the document's spelling of it.</summary>
    private static readonly Dictionary<string, string> _elementTypes = new(StringComparer.Ordinal)
    {
        ["ui-element"] = "UiElement",
        ["data-element"] = "DataElement",
        ["action"] = "Action",
        ["function"] = "Function",
        ["comment"] = "Comment",
    };

    /// <summary>The binding type of each relation, by the document's spelling of it, in the order the notation lists them.</summary>
    private static readonly Dictionary<string, string> _relationTypes = new(StringComparer.Ordinal)
    {
        ["ui-child"] = "UiChild",
        ["owns-action"] = "OwnsAction",
        ["owns-data"] = "OwnsData",
        ["owns-function"] = "OwnsFunction",
        ["shows"] = "Shows",
    };

    /// <summary>The binding's catch-all type for an entry of <c>elements:</c> the notation does not have.</summary>
    public const string UnknownElement = "UnknownElement";

    /// <summary>The binding's catch-all type for an entry of <c>connections:</c> the notation does not have.</summary>
    public const string UnknownConnection = "UnknownConnection";

    /// <summary>The binding's type of an entry that is not a mapping.</summary>
    public const string Unreadable = "Unreadable";

    /// <summary>The binding's type of the body's root, whose <c>version</c> is the header's.</summary>
    public const string Graph = "Graph";

    /// <summary>The element types in the order the notation lists them.</summary>
    public static IReadOnlyList<string> ElementTypes { get; } = [.. _elementTypes.Values];

    /// <summary>The relation types in the order the notation lists them.</summary>
    public static IReadOnlyList<string> RelationTypes { get; } = [.. _relationTypes.Values];

    /// <summary>The relation type a connection's <c>type:</c> names (<c>ui-child</c> is <c>UiChild</c>); null for one the notation does not have.</summary>
    public static string? RelationTypeOf(string documentType) => _relationTypes.GetValueOrDefault(documentType);

    /// <summary>The element type an element's <c>type:</c> names (<c>ui-element</c> is <c>UiElement</c>); null for one the notation does not have.</summary>
    public static string? ElementTypeOf(string documentType) => _elementTypes.GetValueOrDefault(documentType);

    /// <summary>The document's spelling of a type (<c>UiChild</c> is <c>ui-child</c>); the name itself for one it does not spell.</summary>
    public static string DocumentTypeOf(string type) =>
        _relationTypes.Concat(_elementTypes).FirstOrDefault(pair => pair.Value == type).Key ?? type;

    /// <summary>Whether <paramref name="element"/> is an entry of <c>elements:</c> that is a mapping.</summary>
    public static bool IsElement(FblElement element) => element.Type == UnknownElement || _elementTypes.ContainsValue(element.Type);

    /// <summary>Whether <paramref name="element"/> is an entry of <c>connections:</c> that is a mapping.</summary>
    public static bool IsConnection(FblElement element) => element.Type == UnknownConnection || _relationTypes.ContainsValue(element.Type);

    /// <summary>Whether <paramref name="reading"/> is one the tool cannot read at all: not YAML, or a mapping that repeats a key.</summary>
    public static bool IsUnreadable(FblModel reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return reading.Unreadable || reading.Findings.Any(finding => finding.Code == FindingCodes.DuplicateKey);
    }

    /// <summary>A scalar's text as the binding read it as written: null for one that is not there or is not a scalar.</summary>
    public static string? TextOf(FblElement element, string attribute)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Attributes.TryGetValue(attribute, out var value) ? value switch
        {
            null => "",
            string text => text,
            _ => null,
        } : null;
    }

    /// <summary>A number as the tool reads one: zero for one that is not there, is not a scalar or does not read.</summary>
    public static double NumberOf(FblElement element, string attribute) =>
        double.TryParse(TextOf(element, attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>The reading the DISL model of <paramref name="reading"/> is built from.</summary>
    public static FblModel Of(FblModel reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (IsUnreadable(reading)) return new FblModel([], reading.Findings, true);

        var elements = reading.Elements.Where(IsElement).ToList();
        var connections = reading.Elements.Where(IsConnection).ToList();
        var written = elements.Concat(connections).Select(Written).ToHashSet(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var ephemeral = 0;

        var elementIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var read = new List<FblElement>();
        foreach (var element in elements)
        {
            var id = ModelId(Written(element));
            elementIds.TryAdd(Written(element), id);
            read.Add(new FblElement(id, Written(element).Length > 0, element.Type, "element", false, ElementAttributes(element), null, null, null, null, default, element.Line));
        }

        foreach (var connection in connections)
        {
            read.Add(new FblElement(
                ModelId(Written(connection)), Written(connection).Length > 0, connection.Type, "connection", true, ConnectionAttributes(connection), null, null,
                End(TextOf(connection, "from") ?? ""), End(TextOf(connection, "to") ?? ""), default, connection.Line));
        }

        return new FblModel(read, [], false);

        string ModelId(string id)
        {
            if (id.Length > 0 && taken.Add(id)) return id;
            string unnamed;
            do unnamed = $"fdg:unnamed:{++ephemeral}";
            while (written.Contains(unnamed) || !taken.Add(unnamed));
            return unnamed;
        }

        string? End(string id) => elementIds.TryGetValue(id, out var element) ? element : taken.Contains(id) ? null : id;
    }

    /// <summary>The id <paramref name="element"/> is written with; empty for none.</summary>
    public static string Written(FblElement element) => TextOf(element, StoredId) ?? "";

    private static Dictionary<string, object?> ElementAttributes(FblElement element)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["description"] = TextOf(element, "description") ?? "",
            ["x"] = NumberOf(element, "x"),
            ["y"] = NumberOf(element, "y"),
            ["width"] = NumberOf(element, "width"),
            [StoredId] = Written(element),
        };
        if (element.Type == UnknownElement) attributes["documentType"] = TextOf(element, "documentType") ?? "";

        if (element.Type == "Comment")
        {
            attributes["text"] = TextOf(element, "text") ?? "";
            attributes["height"] = NumberOf(element, "height");
            if (TextOf(element, "strayName") is { Length: > 0 } name) attributes["strayName"] = name;
        }
        else
        {
            attributes["name"] = TextOf(element, "name") ?? "";
        }

        return attributes;
    }

    private static Dictionary<string, object?> ConnectionAttributes(FblElement connection)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = TextOf(connection, "name") ?? "",
            ["description"] = TextOf(connection, "description") ?? "",
            [StoredId] = Written(connection),
        };
        if (connection.Type == UnknownConnection) attributes["documentType"] = TextOf(connection, "documentType") ?? "";
        return attributes;
    }
}
