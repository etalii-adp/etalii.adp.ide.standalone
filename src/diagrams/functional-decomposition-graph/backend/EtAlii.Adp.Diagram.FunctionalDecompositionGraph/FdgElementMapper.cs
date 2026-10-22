using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Layout;
using EtAlii.Adp.Specification.Disl;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// An <c>.fdg</c> body's DISL model as the library's elements: the five element types and the five
/// relations, each as the canvas draws them.
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
    public const string UiElementType = Prefix + "ui-element";

    /// <summary>The library type of a Data Element.</summary>
    public const string DataElementType = Prefix + "data-element";

    /// <summary>The library type of an Action.</summary>
    public const string ActionType = Prefix + "action";

    /// <summary>The library type of a Function.</summary>
    public const string FunctionType = Prefix + "function";

    /// <summary>The library type of a Comment.</summary>
    public const string CommentType = Prefix + "comment";

    /// <summary>The library type of a UI child connection.</summary>
    public const string UiChildType = Prefix + "ui-child";

    /// <summary>The library type of an Action connection.</summary>
    public const string OwnsActionType = Prefix + "owns-action";

    /// <summary>The library type of a Data connection.</summary>
    public const string OwnsDataType = Prefix + "owns-data";

    /// <summary>The library type of a Function connection.</summary>
    public const string OwnsFunctionType = Prefix + "owns-function";

    /// <summary>The library type of a Shows connection.</summary>
    public const string ShowsType = Prefix + "shows";

    /// <summary>
    /// The elements and connections a view of <paramref name="viewport"/> should hold: every element
    /// that overlaps it, and every connection whose span does, together with that connection's two
    /// ends so it never arrives without them.
    /// </summary>
    public IReadOnlyList<DiagramElement> Visible(DislModel model, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);

        (IReadOnlyList<Drawn> elements, IReadOnlyList<DrawnConnection> connections) = Drawable(model.Diagram);
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
    private static string ElementTypeOf(string elementType) => Prefix + elementType;

    /// <summary>The library type an FDG relation is drawn as.</summary>
    public static string ConnectionTypeOf(string relation) => Prefix + relation;

    private static (IReadOnlyList<Drawn> Elements, IReadOnlyList<DrawnConnection> Connections) Drawable(DislDiagram diagram)
    {
        // One id space across elements and connections, as the rule set checks it: the first entry
        // with an id is drawn and every later one reusing it is not. Elements are read before
        // connections, whatever the document's order.
        var taken = new HashSet<string>(StringComparer.Ordinal);

        var elements = diagram.Elements
            .Where(element => !element.Type.IsRelation)
            .Select(Drawn.Of)
            .ToList()
            .Where(element => element.Id.Length > 0 && FdgReading.ElementTypes.Contains(element.Type) && taken.Add(element.Id))
            .ToList();

        var elementIds = elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var connections = diagram.Elements
            .Where(element => element.Type.IsRelation)
            .Select(DrawnConnection.Of)
            .ToList()
            .Where(connection => connection.Id.Length > 0
                && FdgReading.RelationTypes.Contains(connection.Type)
                && elementIds.Contains(connection.From)
                && elementIds.Contains(connection.To)
                && taken.Add(connection.Id))
            .ToList();

        return (elements, connections);
    }

    private static bool Overlaps(Drawn element, DiagramViewport viewport) =>
        viewport.Intersects(new Rect(element.X, element.Y, element.Width, element.Height));

    private static bool SpanOverlaps(Drawn from, Drawn to, DiagramViewport viewport) =>
        viewport.Intersects(Hull(new Rect(from.X, from.Y, from.Width, from.Height), new Rect(to.X, to.Y, to.Width, to.Height)));

    private static Rect Hull(Rect a, Rect b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }

    private static DiagramElement Element(Drawn element)
    {
        // The document holds the top-left; the library draws from the centre.
        var payload = new FdgElementPayload
        {
            Name = element.Name,
            Text = element.Text,
            Width = element.Width,
            Height = element.Height,
        };

        return Pack(
            element.Id,
            element.X + (element.Width / 2),
            element.Y + (element.Height / 2),
            ElementTypeOf(FdgReading.DocumentTypeOf(element.Type)),
            payload);
    }

    private static DiagramElement Connection(DrawnConnection connection)
    {
        // A connection has no position of its own; it follows its ends.
        var payload = new FdgConnectionPayload
        {
            FromElementId = connection.From,
            ToElementId = connection.To,
            Name = connection.Name,
        };

        return Pack(connection.Id, 0d, 0d, ConnectionTypeOf(FdgReading.DocumentTypeOf(connection.Type)), payload);
    }

    private static DiagramElement Pack(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());

    /// <summary>An element as it is drawn: its written id, its type, its label and its box.</summary>
    /// <remarks>A Comment shows its text and has its own height; the other four show their name and share the definition's fixed height (<see cref="FdgDefinition.SharedHeight"/>).</remarks>
    private sealed record Drawn(string Id, string Type, string Name, string Text, double X, double Y, double Width, double Height)
    {
        public static Drawn Of(DislElement element)
        {
            var isComment = FdgDefinition.IsComment(element);
            return new Drawn(
                FdgDefinition.WrittenId(element),
                element.Type.Name,
                isComment ? "" : FdgDefinition.TextOf(element, "name"),
                isComment ? FdgDefinition.TextOf(element, "text") : "",
                FdgDefinition.NumberOf(element, "x"),
                FdgDefinition.NumberOf(element, "y"),
                FdgDefinition.NumberOf(element, "width"),
                isComment ? FdgDefinition.NumberOf(element, "height") : FdgDefinition.SharedHeight);
        }
    }

    /// <summary>A connection as it is drawn: its written id, its relation, its ends as written and its name.</summary>
    private sealed record DrawnConnection(string Id, string Type, string From, string To, string Name)
    {
        public static DrawnConnection Of(DislElement connection) =>
            new(FdgDefinition.WrittenId(connection), connection.Type.Name, connection.SourceId ?? "", connection.TargetId ?? "", FdgDefinition.TextOf(connection, "name"));
    }
}
