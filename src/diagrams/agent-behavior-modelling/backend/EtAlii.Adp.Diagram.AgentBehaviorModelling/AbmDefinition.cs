using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// The behavior model's bundled DISL definition (<c>definition/agent-behavior-modelling.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, and what the providers derive from it: the palette, the
/// context menus and the property rows, mapped onto the host's types with the wire ids of its
/// <c>x-abm</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>A node's DISL type is the one <c>x-abm.types</c> maps to its kind</b>: <c>Sequence</c> for
/// <c>sequence</c>, <c>Do</c> for <c>action</c>, and so on for the eleven. The persistence plugin reads
/// and writes the types through these two maps, so the Markdown's keywords stay the module's.
/// </para>
/// <para>
/// <b>The model is the document's own</b>: <see cref="AbmBody.Disl"/>, kept with the bytes it was built
/// from. A node's id is its place, which the definition derives and the module addresses it by.
/// </para>
/// <para>
/// <b>The code wins where the two would differ</b>, and three host choices stay here: <c>env.readOnly</c>
/// is always false, because the module never offered a read-only menu; a shortcut is passed as the text
/// the definition writes (<c>Alt+Up</c>), as the module always sent it; and Arrange carries its sentence
/// even while it is available.
/// </para>
/// </remarks>
internal static class AbmDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(AbmDefinition).Assembly, "agent-behavior-modelling.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-abm"));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LoadedKindOfType = new(() =>
        Ids.Types.Where(pair => AbmNodeKinds.IsKnown(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LoadedTypeOfKind = new(() =>
        KindOfType.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal));

    private static readonly Lazy<IReadOnlyList<ToolboxItemDefinition>> LoadedToolbox = new(() =>
    [
        .. ToolboxDerivation.Derive(Specification, Ids)
            .Select(tool => new ToolboxItemDefinition(tool.Id, tool.Label, tool.Icon, tool.Description, tool.DropActionId ?? "")),
    ]);

    private static readonly Lazy<string> LoadedArrangeReason = new(ArrangeReasonOf);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-abm</c>.</summary>
    private static WireIdMap Ids => LoadedIds.Value;

    /// <summary>The kind of each node type: <c>Do</c> is <c>action</c>.</summary>
    public static IReadOnlyDictionary<string, string> KindOfType => LoadedKindOfType.Value;

    /// <summary>The node type of each kind: <c>action</c> is <c>Do</c>.</summary>
    public static IReadOnlyDictionary<string, string> TypeOfKind => LoadedTypeOfKind.Value;

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => LoadedToolbox.Value;

    /// <summary>What <c>env</c> reads: editable, always, as the module's menus and rows have always been.</summary>
    public static DislEnv Env { get; } = new();

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="entry"/>: a node, a placement, a parent-line gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(AbmDocumentEntry entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = entry.Document.Disl.Diagram;
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.TryParsePlacement(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : GestureIds.TryParseRelation(elementId, out var from, out var to)
                    ? DislMenuTarget.Connection(diagram, ElementOf(diagram, from), ElementOf(diagram, to), "Child")
                    : null;
        if (target is null) return [];

        return
        [
            .. ContextMenuDerivation.Derive(Specification, target, Env, Ids)
                .Select(group => new ContextActionGroupDefinition([.. group.Entries.Select(Action)])),
        ];
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="entry"/>: a node's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(AbmDocumentEntry entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ElementOf(entry.Document.Disl.Diagram, elementId) is { } element
            ? [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(Row)]
            : [];
    }

    /// <summary>
    /// The id a node an operation creates has in the transaction's working model. A node's id is its place,
    /// which the definition derives once the document is read again; nothing is written to name it.
    /// </summary>
    public static IIdSource NewIds => DislIds.Fixed("new");

    /// <summary>Writes <paramref name="transaction"/>'s changes to <paramref name="document"/> in order, or gives its refusal.</summary>
    public static AbmEdit Apply(AbmBody document, DislTransaction transaction) =>
        Apply(document, transaction, change => change);

    /// <summary>
    /// Writes <paramref name="transaction"/>'s changes to <paramref name="document"/> in order, each as
    /// <paramref name="written"/> makes it from the FBL change the definition writes, or gives its refusal.
    /// </summary>
    public static AbmEdit Apply(AbmBody document, DislTransaction transaction, Func<ModelChange, ModelChange> written)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(written);
        if (!transaction.WasApplied) return AbmEdit.Refused(transaction.Refusal!);

        foreach (var change in transaction.Changes)
        {
            var edit = document.Change(written(DislWrite.ToFbl(Specification, change)));
            if (!edit.WasApplied) return edit;
        }
        return AbmEdit.Applied;
    }

    /// <summary>The node <paramref name="id"/> names - its place - or null.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return diagram.Nodes.FirstOrDefault(node => node.Id == id);
    }

    private static ContextActionDefinition Action(DerivedMenuEntry entry) => new(
        entry.Id,
        entry.Label,
        entry.Icon,
        entry.Shortcut is { } key ? new ContextShortcutDefinition(key.Text) : null,
        entry.Available,
        // Arrange carries its sentence even while it is available, as the hand-written menu did; the
        // client shows a reason only for an unavailable action.
        entry is { Available: true, Operation: "arrange" or "arrangeFromNode" } ? LoadedArrangeReason.Value : entry.UnavailableReason);

    /// <summary>
    /// A row as the grid draws it: a retype row is the Choice editor with the kinds it can become, a
    /// <c>textarea</c> the Text editor, anything else a line.
    /// </summary>
    private static ContextPropertyDefinition Row(DerivedRow row)
    {
        var editor = row.Retypes
            ? ContextPropertyEditor.Choice
            : row.Widget == "textarea"
                ? ContextPropertyEditor.Text
                : ContextPropertyEditor.Line;
        return new ContextPropertyDefinition(row.Id, row.Label, row.Value, editor, row.ReadOnlyReason, row.Group,
            row.Retypes ? row.Candidates ?? [] : null);
    }

    /// <summary>The sentence of Arrange's one <c>unavailable</c> reason, as the definition writes it.</summary>
    private static string ArrangeReasonOf() =>
        Specification.Root.GetProperty("behavior").GetProperty("operations").GetProperty("arrange").GetProperty("unavailable")
            .EnumerateArray().Select(reason => reason.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null)
            .FirstOrDefault(message => message is not null) ?? "";
}
