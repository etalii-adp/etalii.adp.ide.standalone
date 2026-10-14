using System.Globalization;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The causal loop diagram's bundled DISL definition (<c>definition/causal-loop-diagram.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, and what the providers derive from it: the palette, the context
/// menus, the property rows and the deletion confirmation, mapped onto the host's types with the wire
/// ids of its <c>x-cld</c> block (causal-loop-disl-fbl Requirement 3.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the parser's reading</b>, put into DISL by <see cref="CausalLoopDislAdapter"/> until the
/// body is read through the binding. An element is found by its wire id, which is its model id: the
/// definition's natural ids with their prefixes. Of two elements with one id the first is found, as the
/// hand-written providers found the first.
/// </para>
/// <para>
/// <b>A document that could not be read offers nothing</b>, neither a menu nor a row: its emptiness is
/// not the document, so nothing may be written over it, and the host offers no action that would only refuse.
/// </para>
/// </remarks>
internal static class CausalLoopDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(CausalLoopDefinition).Assembly, "causal-loop-diagram.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-cld"));

    private static readonly Lazy<IReadOnlyList<ToolboxItemDefinition>> LoadedToolbox = new(() =>
    [
        .. ToolboxDerivation.Derive(Specification, Ids)
            .Select(tool => new ToolboxItemDefinition(tool.Id, tool.Label, tool.Icon, tool.Description, tool.DropActionId ?? "")),
    ]);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LoadedStandingReasons = new(StandingReasonsOf);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-cld</c>.</summary>
    private static WireIdMap Ids => LoadedIds.Value;

    /// <summary>What <c>env</c> reads for a readable document: editable, with no viewpoint.</summary>
    private static DislEnv Env { get; } = new();

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => LoadedToolbox.Value;

    /// <summary>The model of <paramref name="entry"/>.</summary>
    public static DislDiagram DiagramOf(DocumentEntry<CausalLoopDocument, CausalLoopModel> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return CausalLoopDislAdapter.Of(entry.Model);
    }

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="entry"/>: an element, a placement, a connect gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<CausalLoopDocument, CausalLoopModel> entry, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsUsable) return [];

        var diagram = DiagramOf(entry);
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.IsPlacement(elementId)
                ? DislMenuTarget.Canvas(diagram)
                : GestureIds.TryParseRelation(elementId, out var from, out var to)
                    ? DislMenuTarget.Connection(diagram, VariableOf(diagram, from), VariableOf(diagram, to), "CausalLink")
                    : null;
        if (target is null) return [];

        return
        [
            .. ContextMenuDerivation.Derive(Specification, target, Env, Ids)
                .Select(group => new ContextActionGroupDefinition([.. group.Entries.Select(Action)])),
        ];
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="entry"/>: an element's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<CausalLoopDocument, CausalLoopModel> entry, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsUsable || ElementOf(DiagramOf(entry), elementId) is not { } element) return [];
        return [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(row => Row(WithNumbersShownAsTheToolShowsThem(element, row)))];
    }

    /// <summary>The confirmation removing the element <paramref name="elementId"/> asks for; null when it names none, or asks nothing.</summary>
    public static DislConfirmation? RemovalConfirmation(DocumentEntry<CausalLoopDocument, CausalLoopModel> entry, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ElementOf(DiagramOf(entry), elementId) is { } element ? DeletionPolicy.Confirmation(Specification, element, env: Env) : null;
    }

    /// <summary>The element <paramref name="id"/> names: the first in model order with that id; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string? id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return id is null ? null : diagram.ElementById(id);
    }

    /// <summary>The variable written with <paramref name="name"/>; null for none.</summary>
    private static DislElement? VariableOf(DislDiagram diagram, string name) =>
        diagram.NodesOfType("Variable").FirstOrDefault(variable => variable.Attributes.TryGetValue("name", out var written) && written as string == name);

    private static ContextActionDefinition Action(DerivedMenuEntry entry) => new(
        entry.Id,
        entry.Label,
        entry.Icon,
        entry.Shortcut is { } key ? new ContextShortcutDefinition(key.Key, key.Ctrl, key.Shift, key.Alt, key.Meta) : null,
        entry.Available,
        // An operation that can be unavailable carries its sentence even while it is available, as the
        // hand-written menu did; the client shows a reason only for an unavailable action.
        entry.Available && entry.Operation is { } operation && LoadedStandingReasons.Value.TryGetValue(operation, out var standing) ? standing : entry.UnavailableReason);

    /// <summary>
    /// A row as the grid draws it: a <c>select</c> is a Choice among its candidates, a <c>switch</c> a
    /// Toggle, anything else a line.
    /// </summary>
    private static ContextPropertyDefinition Row(DerivedRow row)
    {
        var editor = row.Widget switch
        {
            "select" => ContextPropertyEditor.Choice,
            "switch" => ContextPropertyEditor.Toggle,
            _ => ContextPropertyEditor.Line,
        };
        var candidates = editor is ContextPropertyEditor.Choice ? row.Candidates ?? [] : null;
        return new ContextPropertyDefinition(row.Id, row.Label, row.Value, editor, row.ReadOnlyReason, row.Group, candidates);
    }

    /// <summary>
    /// A row of a <c>number</c> attribute with its value in .NET's shortest round-trip form, <c>1E+21</c>,
    /// as the tool has always shown a weight. A row's text is CEL's <c>string(double)</c>, which is
    /// Go's <c>1e+21</c>; the two agree on ordinary values. The definition's companion records the
    /// difference (Other differences: numbers, finding B11); every other row is the definition's.
    /// </summary>
    private static DerivedRow WithNumbersShownAsTheToolShowsThem(DislElement element, DerivedRow row)
    {
        foreach (var (name, stored) in element.Attributes)
        {
            if (stored is double number && Ids.PropertyId(name) == row.Id) return row with { Value = number.ToString(CultureInfo.InvariantCulture) };
        }
        return row;
    }

    /// <summary>The sentence of each operation's first <c>unavailable</c> reason that is a plain message, by operation.</summary>
    private static IReadOnlyDictionary<string, string> StandingReasonsOf()
    {
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var operation in Specification.Root.GetProperty("behavior").GetProperty("operations").EnumerateObject())
        {
            if (!operation.Value.TryGetProperty("unavailable", out var unavailable) || unavailable.ValueKind != JsonValueKind.Array) continue;
            if (unavailable.EnumerateArray()
                    .Select(reason => reason.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null)
                    .FirstOrDefault(message => message is not null) is { } sentence)
            {
                reasons[operation.Name] = sentence;
            }
        }
        return reasons;
    }
}
