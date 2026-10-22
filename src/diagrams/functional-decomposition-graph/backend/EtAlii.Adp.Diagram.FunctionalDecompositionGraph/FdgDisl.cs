using System.Runtime.CompilerServices;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Builds the DISL model of an <see cref="FdgModel"/>: from what <see cref="FdgParser"/> read, handed to
/// <see cref="DislModelBuilder"/> as the binding's reading would be, so that what is derived changes and
/// how the body is read does not. Deleted once the body is read through the module's FBL binding
/// (functional-decomposition-graph-disl-fbl task 9).
/// </summary>
/// <remarks>
/// <para>
/// <b>Each entry is read under the binding's type</b> (<c>UiElement</c>, <c>OwnsData</c>, …), which
/// the definition's <c>typeMap</c> maps onto the metamodel; an entry whose <c>type:</c> names no type
/// of the notation is read as <c>UnknownElement</c> or <c>UnknownConnection</c> with the type as written
/// in <c>documentType</c>. A Comment's stray <c>name</c> is read as the binding's <c>strayName</c>.
/// </para>
/// <para>
/// <b>Model ids are unique; written ids need not be.</b> The first entry with a written id keeps it,
/// elements before connections, and a later one, or one written without an id, gets an ephemeral id no
/// entry is written with. The written id travels as the host attribute <see cref="StoredId"/>, which
/// <see cref="FdgDefinition.ElementOf"/> finds an element by, as <see cref="FdgModel"/> does.
/// </para>
/// <para>
/// <b>A connection's end resolves to the first element written with that id</b>, never to a
/// connection, as the hand-written rules resolve it; an end that names no element is kept as written,
/// unless it names a connection, which it then cannot be told apart from.
/// </para>
/// <para>
/// <b>The numbers are the parser's</b>: zero where the document has none or one that does not read,
/// and a Comment's height as stored.
/// </para>
/// </remarks>
internal static class FdgDisl
{
    /// <summary>The host attribute holding the id an entry is written with, empty for none.</summary>
    public const string StoredId = "storedId";

    private static readonly ConditionalWeakTable<FdgModel, DislDiagram> _built = [];

    private static readonly Dictionary<string, string> _elementTypes = new(StringComparer.Ordinal)
    {
        [FdgElementTypes.UiElement] = "UiElement",
        [FdgElementTypes.DataElement] = "DataElement",
        [FdgElementTypes.Action] = "Action",
        [FdgElementTypes.Function] = "Function",
        [FdgElementTypes.Comment] = "Comment",
    };

    private static readonly Dictionary<string, string> _relationTypes = new(StringComparer.Ordinal)
    {
        [FdgConnectionTypes.UiChild] = "UiChild",
        [FdgConnectionTypes.OwnsAction] = "OwnsAction",
        [FdgConnectionTypes.OwnsData] = "OwnsData",
        [FdgConnectionTypes.OwnsFunction] = "OwnsFunction",
        [FdgConnectionTypes.Shows] = "Shows",
    };

    /// <summary>The relation type a connection's <c>type:</c> names (<c>ui-child</c> is <c>UiChild</c>); null for one the notation does not have.</summary>
    public static string? RelationTypeOf(string documentType) => _relationTypes.GetValueOrDefault(documentType);

    /// <summary>The document's spelling of a relation type (<c>UiChild</c> is <c>ui-child</c>); the name itself for one it does not spell.</summary>
    public static string DocumentTypeOf(string relationType) =>
        _relationTypes.FirstOrDefault(pair => pair.Value == relationType).Key ?? relationType;

    /// <summary>The model of <paramref name="model"/>, built once per parsed model; read it, never change it.</summary>
    public static DislDiagram Of(FdgModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _built.GetValue(model, Build);
    }

    /// <summary>A model of <paramref name="model"/> of its own.</summary>
    private static DislDiagram Build(FdgModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var written = model.Elements.Select(element => element.Id).Concat(model.Connections.Select(connection => connection.Id)).ToHashSet(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var ephemeral = 0;

        var elementIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var read = new List<FblElement>();
        foreach (var element in model.Elements)
        {
            var id = ModelId(element.Id);
            elementIds.TryAdd(element.Id, id);
            read.Add(new FblElement(id, element.Id.Length > 0, ElementType(element), "element", false, ElementAttributes(element), null, null, null, null, default, element.Range.Start + 1));
        }

        foreach (var connection in model.Connections)
        {
            read.Add(new FblElement(
                ModelId(connection.Id), connection.Id.Length > 0, RelationType(connection), "connection", true, ConnectionAttributes(connection), null, null,
                End(connection.From), End(connection.To), default, connection.Range.Start + 1));
        }

        return DislModelBuilder.From(new FblModel(read, [], false), FdgDefinition.Specification).Diagram;

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

    private static string ElementType(FdgElement element) => _elementTypes.GetValueOrDefault(element.Type, "UnknownElement");

    private static string RelationType(FdgConnection connection) => _relationTypes.GetValueOrDefault(connection.Type, "UnknownConnection");

    private static Dictionary<string, object?> ElementAttributes(FdgElement element)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["description"] = element.Description,
            ["x"] = element.X,
            ["y"] = element.Y,
            ["width"] = element.Width,
            [StoredId] = element.Id,
        };
        if (!_elementTypes.ContainsKey(element.Type)) attributes["documentType"] = element.Type;

        if (element.IsComment)
        {
            attributes["text"] = element.Text;
            attributes["height"] = element.Height;
            if (element.Name.Length > 0) attributes["strayName"] = element.Name;
        }
        else
        {
            attributes["name"] = element.Name;
        }

        return attributes;
    }

    private static Dictionary<string, object?> ConnectionAttributes(FdgConnection connection)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = connection.Name,
            ["description"] = connection.Description,
            [StoredId] = connection.Id,
        };
        if (!_relationTypes.ContainsKey(connection.Type)) attributes["documentType"] = connection.Type;
        return attributes;
    }
}
