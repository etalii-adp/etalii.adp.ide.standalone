using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The hype cycle graph's bundled DISL definition (<c>definition/gartner-hype-cycle-graph.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, and what the providers derive from it: the palette, the
/// context menus and the property rows, mapped onto the host's types with the wire ids of its
/// <c>x-ghg</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the document's own</b>: <see cref="GhgBody.Disl"/>, built from the binding's
/// reading and kept with the bytes it was built from, so a menu and a row read the same snapshot and
/// no call builds it twice.
/// </para>
/// <para>
/// <b>An element is found by the id written in the document</b>, as <see cref="GhgEdits"/> finds it:
/// the first trend with it, else the first trigger, else the first note, else the first influence. A
/// later entry reusing an id, or one without an id, is told apart by FBL by its place; the client
/// knows it by what is written, the binding's <c>storedId</c>.
/// </para>
/// <para>
/// <b>The menus are derived in true time</b> (<c>env.viewpoint</c>): the backend has no viewpoint, and the
/// canvas's own menu is compact's concern. <b>Read-only</b> (<c>env.readOnly</c>) is an entry that could not be read.
/// </para>
/// </remarks>
internal static class GhgDefinition
{
    /// <summary>The viewpoint the backend derives in.</summary>
    public const string Viewpoint = "trueTime";

    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(GhgDefinition).Assembly, "gartner-hype-cycle-graph.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-ghg"));

    private static readonly Lazy<IReadOnlyList<ToolboxItemDefinition>> LoadedToolbox = new(() =>
    [
        .. ToolboxDerivation.Derive(Specification, Ids)
            .Select(tool => new ToolboxItemDefinition(tool.Id, tool.Label, tool.Icon, tool.Description, tool.DropActionId ?? "")),
    ]);

    private static readonly Lazy<string> LoadedArrangeReason = new(ArrangeReasonOf);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-ghg</c>.</summary>
    public static WireIdMap Ids => LoadedIds.Value;

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => LoadedToolbox.Value;

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="entry"/>: an element, a placement, a connect gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(GhgDocumentEntry entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = entry.Document.Disl.Diagram;
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.TryParsePlacement(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : GhgGestures.TryParseRelation(elementId, out var from, out _, out var to, out _)
                    ? DislMenuTarget.Connection(diagram, ElementOf(diagram, from), ElementOf(diagram, to), "Influence")
                    : null;
        if (target is null) return [];

        return
        [
            .. ContextMenuDerivation.Derive(Specification, target, Env(entry), Ids)
                .Select(group => new ContextActionGroupDefinition([.. group.Entries.Select(Action)])),
        ];
    }

    /// <summary>The element <paramref name="id"/> names, as <see cref="GhgEdits"/> finds it; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        foreach (var type in (string[])["Trend", "Trigger", "Note"])
        {
            if (diagram.NodesOfType(type).FirstOrDefault(node => WrittenId(node) == id) is { } node) return node;
        }

        return diagram.RelationsOfType("Influence").FirstOrDefault(relation => WrittenId(relation) == id);
    }

    /// <summary>The id written in the document, the binding's <c>storedId</c>: empty for an entry without one.</summary>
    public static string WrittenId(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.HostAttributes.TryGetValue("storedId", out var stored) ? GhgParser.Text(stored) ?? "" : "";
    }

    private static DislEnv Env(GhgDocumentEntry entry) => new(ReadOnly: !entry.IsUsable, Viewpoint: Viewpoint);

    private static ContextActionDefinition Action(DerivedMenuEntry entry) => new(
        entry.Id,
        entry.Label,
        entry.Icon,
        entry.Shortcut is { } key ? new ContextShortcutDefinition(key.Key, key.Ctrl, key.Shift, key.Alt, key.Meta) : null,
        entry.Available,
        // Arrange carries its sentence even while it is available, as the hand-written menu did; the
        // client shows a reason only for an unavailable action.
        entry.Available && entry.Operation == "arrange" ? LoadedArrangeReason.Value : entry.UnavailableReason);

    /// <summary>The sentence of Arrange's one <c>unavailable</c> reason, as the definition writes it.</summary>
    private static string ArrangeReasonOf() =>
        Specification.Root.GetProperty("behavior").GetProperty("operations").GetProperty("arrange").GetProperty("unavailable")
            .EnumerateArray().Select(reason => reason.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null)
            .FirstOrDefault(message => message is not null) ?? "";
}
