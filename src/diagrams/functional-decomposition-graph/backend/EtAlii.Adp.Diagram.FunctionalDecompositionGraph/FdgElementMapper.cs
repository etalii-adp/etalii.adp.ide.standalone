using Google.Protobuf;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// An <c>.fdg</c> model as the library's elements: the five element types and the five relations,
/// each as the canvas draws them.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is sent</b>: the type, the element's centre, and a payload carrying its name or text and
/// its width and height. Size travels in the payload because the core element has nowhere else to
/// hold it, and it must, so that a resize changes the payload bytes the shared diff compares.
/// </para>
/// <para>
/// <b>A Description is never sent</b> (Requirement 7.1). <see cref="FdgElementPayload"/> and
/// <see cref="FdgConnectionPayload"/> have no field for it, so none can be drawn - and a change to a
/// Description alone changes nothing sent, so it produces no delta at all.
/// </para>
/// <para>
/// <b>What a rule would refuse is still drawn, and what cannot be drawn is left out.</b> A document
/// that breaks a rule opens with everything readable on the canvas, and the validator names the
/// breach (Requirement 5.5). An entry whose type this notation does not have, a connection whose
/// endpoint is not an element, and every later entry reusing an id already drawn are left out
/// instead. The last one is not a choice: the shared diff throws on an id that appears twice in one
/// rendering, and a document may repeat an id - <c>fdg.duplicate-id</c> reports it.
/// </para>
/// </remarks>
public sealed class FdgElementMapper
{
    private const string Prefix = "etalii/functional-decomposition-graph+";

    /// <summary>The library type of a UI Element.</summary>
    public const string UiElementType = Prefix + FdgElementTypes.UiElement;

    /// <summary>The library type of a Data Element.</summary>
    public const string DataElementType = Prefix + FdgElementTypes.DataElement;

    /// <summary>The library type of an Action.</summary>
    public const string ActionType = Prefix + FdgElementTypes.Action;

    /// <summary>The library type of a Function.</summary>
    public const string FunctionType = Prefix + FdgElementTypes.Function;

    /// <summary>The library type of a Comment.</summary>
    public const string CommentType = Prefix + FdgElementTypes.Comment;

    /// <summary>The library type of a UI child connection.</summary>
    public const string UiChildType = Prefix + FdgConnectionTypes.UiChild;

    /// <summary>The library type of an Action connection.</summary>
    public const string OwnsActionType = Prefix + FdgConnectionTypes.OwnsAction;

    /// <summary>The library type of a Data connection.</summary>
    public const string OwnsDataType = Prefix + FdgConnectionTypes.OwnsData;

    /// <summary>The library type of a Function connection.</summary>
    public const string OwnsFunctionType = Prefix + FdgConnectionTypes.OwnsFunction;

    /// <summary>The library type of a Shows connection.</summary>
    public const string ShowsType = Prefix + FdgConnectionTypes.Shows;

    /// <summary>
    /// The elements and connections a view of <paramref name="viewport"/> should hold: every element
    /// that overlaps it, and every connection whose span does, together with that connection's two
    /// ends so it never arrives without them.
    /// </summary>
    public IReadOnlyList<DiagramElement> Visible(FdgModel model, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);

        (IReadOnlyList<FdgElement> elements, IReadOnlyList<FdgConnection> connections) = Drawable(model);
        var byId = elements.ToDictionary(element => element.Id, StringComparer.Ordinal);

        var shownIds = elements
            .Where(element => Overlaps(element, viewport))
            .Select(element => element.Id)
            .ToHashSet(StringComparer.Ordinal);

        var shownConnections = connections
            .Where(connection => SpanOverlaps(byId[connection.From], byId[connection.To], viewport))
            .ToArray();

        foreach (var connection in shownConnections)
        {
            shownIds.Add(connection.From);
            shownIds.Add(connection.To);
        }

        return
        [
            .. elements.Where(element => shownIds.Contains(element.Id)).Select(Element),
            .. shownConnections.Select(Connection),
        ];
    }

    /// <summary>The library type an FDG element type is drawn as.</summary>
    public static string ElementTypeOf(string elementType) => Prefix + elementType;

    /// <summary>The library type an FDG relation is drawn as.</summary>
    public static string ConnectionTypeOf(string relation) => Prefix + relation;

    private static (IReadOnlyList<FdgElement> Elements, IReadOnlyList<FdgConnection> Connections) Drawable(FdgModel model)
    {
        // One id space across elements and connections, as the rule set checks it: the first entry
        // with an id is drawn and every later one reusing it is not.
        var taken = new HashSet<string>(StringComparer.Ordinal);

        var elements = model.Elements
            .Where(element => element.Id.Length > 0 && FdgElementTypes.IsKnown(element.Type) && taken.Add(element.Id))
            .ToList();

        var elementIds = elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var connections = model.Connections
            .Where(connection => connection.Id.Length > 0
                && FdgConnectionTypes.IsKnown(connection.Type)
                && elementIds.Contains(connection.From)
                && elementIds.Contains(connection.To)
                && taken.Add(connection.Id))
            .ToList();

        return (elements, connections);
    }

    private static bool Overlaps(FdgElement element, DiagramViewport viewport) =>
        element.X + element.Width >= viewport.MinX
        && element.X <= viewport.MaxX
        && element.Y + element.DrawnHeight >= viewport.MinY
        && element.Y <= viewport.MaxY;

    private static bool SpanOverlaps(FdgElement from, FdgElement to, DiagramViewport viewport) =>
        Math.Max(from.X + from.Width, to.X + to.Width) >= viewport.MinX
        && Math.Min(from.X, to.X) <= viewport.MaxX
        && Math.Max(from.Y + from.DrawnHeight, to.Y + to.DrawnHeight) >= viewport.MinY
        && Math.Min(from.Y, to.Y) <= viewport.MaxY;

    private static DiagramElement Element(FdgElement element)
    {
        // The document holds the top-left; the library draws from the centre.
        var payload = new FdgElementPayload
        {
            Name = element.IsComment ? "" : element.Name,
            Text = element.IsComment ? element.Text : "",
            Width = element.Width,
            Height = element.DrawnHeight,
        };

        return Pack(
            element.Id,
            element.X + (element.Width / 2),
            element.Y + (element.DrawnHeight / 2),
            ElementTypeOf(element.Type),
            payload);
    }

    private static DiagramElement Connection(FdgConnection connection)
    {
        // A connection has no position of its own; it follows its ends.
        var payload = new FdgConnectionPayload
        {
            FromElementId = connection.From,
            ToElementId = connection.To,
            Name = connection.Name,
        };

        return Pack(connection.Id, 0d, 0d, ConnectionTypeOf(connection.Type), payload);
    }

    private static DiagramElement Pack(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
