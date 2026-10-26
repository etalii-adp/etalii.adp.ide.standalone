using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The supply chain diagram's bundled DISL definition (<c>definition/supply-chain.dis</c>, from etalii-adp/etalii.adp),
/// loaded once as a <see cref="DislDiagramDefinition"/> with <see cref="SupplyChainWritten"/>, and what the providers
/// derive from it: the palette, the context menus, the property rows and what removing a node asks first.
/// </summary>
/// <remarks>
/// <para>
/// <b>An element is found by the id it is written with</b>, as <see cref="SupplyChainModel"/> finds it: the first node
/// with it, else the first flow, else the first group. The empty id finds the first entry written without one, as the model does; an id nothing is written with has no menu.
/// </para>
/// <para>
/// <b>Arrange and the two decreases carry their sentence even while they are available</b>, as the hand-written
/// menus did.
/// </para>
/// </remarks>
internal static class SupplyChainDefinition
{
    /// <summary>The definition, with its <c>x-supply-chain</c> wire ids; core's <see cref="DislToolboxProvider"/> hands out its palette.</summary>
    internal static DislDiagramDefinition Definition { get; } = new(
        typeof(SupplyChainDefinition).Assembly,
        "supply-chain.dis",
        "x-supply-chain",
        SupplyChainWritten.Plugins,
        ["arrange", "decreaseQuantity", "decreaseVolume"]);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => Definition.Toolbox;

    /// <summary>What <c>env</c> reads for a menu and a form: editable, in no viewpoint.</summary>
    private static DislEnv Env { get; } = new();

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="model"/>: an element, a placement, a connect gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(SupplyChainModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        var diagram = SupplyChainDisl.Of(model).Diagram;
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.TryParsePlacement(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : GestureIds.TryParseRelation(elementId, out var from, out var to)
                    ? DislMenuTarget.Connection(diagram, NodeOf(diagram, from), NodeOf(diagram, to), "Flow")
                    : null;
        return target is null ? [] : Definition.Menus(target, Env);
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="model"/>: a node's, a flow's or a group's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(SupplyChainModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ElementOf(SupplyChainDisl.Of(model).Diagram, elementId) is { } element ? Definition.Rows(element, Env) : [];
    }

    /// <summary>What removing <paramref name="elementId"/> asks first, the definition's <c>behavior.deletion</c>: a node with a flow written to or from it; none otherwise.</summary>
    public static RemovalConfirmation? RemovalConfirmation(SupplyChainModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ElementOf(SupplyChainDisl.Of(model).Diagram, elementId) is { } element && DeletionPolicy.Confirmation(Specification, element, env: Env) is { } asked
            ? new RemovalConfirmation(asked.Title, asked.Message, asked.ConfirmLabel, asked.Danger)
            : null;
    }

    /// <summary>The element <paramref name="id"/> names, as <see cref="SupplyChainModel"/> finds it: the first node written with it, else the first flow, else the first group; null for none.</summary>
    private static DislElement? ElementOf(DislDiagram diagram, string? id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return id is null
            ? null
            : NodeOf(diagram, id)
                ?? diagram.RelationsOfType("Flow").FirstOrDefault(flow => SupplyChainWritten.IdOf(flow) == id)
                ?? diagram.NodesOfType("Group").FirstOrDefault(group => SupplyChainWritten.IdOf(group) == id);
    }

    private static DislElement? NodeOf(DislDiagram diagram, string? id) =>
        id is not null ? diagram.NodesOfType("Stage").FirstOrDefault(node => SupplyChainWritten.IdOf(node) == id) : null;
}
