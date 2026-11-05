using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// The palette, the context menus, the property rows, what removing a node asks first and the findings, as the module
/// wrote them by hand before they were derived from the DISL definition (supply-chain-disl-fbl, tasks 5 and 6), kept verbatim as the oracle
/// the derived ones are compared with, element for element.
/// </summary>
internal static class HandWrittenSupplyChain
{
    private const string RenameActionId = "supply-chain.rename";
    private const string IncreaseActionId = "supply-chain.increase";
    private const string DecreaseActionId = "supply-chain.decrease";
    private const string GroupActionId = "supply-chain.group";
    private const string RemoveActionId = "supply-chain.remove";
    private const string ArrangeActionId = "supply-chain.arrange";
    private const string ConnectActionId = "supply-chain.connect";
    private const string AddGroupActionId = "supply-chain.add-group";
    private const string NoGroup = "(none)";
    private const string IdentityGroup = "Identity";
    private const string AmountGroup = "Amount";
    private const string GroupIcon = "mdi-group";

    /// <summary>The palette, as the hand-written <c>SupplyChainToolboxProvider.Items</c> listed it.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox { get; } =
    [
        Item(SupplyChainNodeTypes.Source, "Where something enters the chain: a mine, a well, a farm, a data feed."),
        Item(SupplyChainNodeTypes.Processor, "Turns what it receives into material others can use."),
        Item(SupplyChainNodeTypes.Producer, "Makes the parts or components something is built from."),
        Item(SupplyChainNodeTypes.Integrator, "Combines parts into the finished whole."),
        Item(SupplyChainNodeTypes.Hub, "Holds and moves what is finished on to where it is wanted."),
        Item(SupplyChainNodeTypes.Outlet, "Where the finished whole is offered to those who use it."),
        Item(SupplyChainNodeTypes.Consumer, "Where the chain ends: whoever uses what it delivers."),
        new("supply-chain.toolbox.group", "Group", GroupIcon, "A frame for a region, a company or a tier; drag nodes into it.", AddGroupActionId),
    ];

    /// <summary>The context menu of <paramref name="elementId"/>, as <c>SupplyChainContextActionProvider.Discover</c> answered it.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(SupplyChainModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        var arrange = new ContextActionDefinition(
            ArrangeActionId, "Arrange diagram", "mdi-sitemap-outline", null,
            model.Nodes.Count > 0, "There is nothing to arrange until this diagram has a node.");

        if (model.NodeOf(elementId) is { } node)
        {
            var atZero = (node.Quantity ?? 0) <= 0;
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(IncreaseActionId, "Increase quantity", "mdi-plus-circle-outline", new ContextShortcutDefinition("+")),
                    new ContextActionDefinition(DecreaseActionId, "Decrease quantity", "mdi-minus-circle-outline", new ContextShortcutDefinition("-"), !atZero, "The quantity is already zero."),
                ]),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(GroupActionId, "Put in a new group…", GroupIcon),
                    new ContextActionDefinition(RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
                new ContextActionGroupDefinition([arrange]),
            ];
        }

        if (model.FlowOf(elementId) is { } flow)
        {
            var atZero = (flow.Volume ?? 0) <= 0;
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(IncreaseActionId, "Increase volume", "mdi-plus-circle-outline", new ContextShortcutDefinition("+")),
                    new ContextActionDefinition(DecreaseActionId, "Decrease volume", "mdi-minus-circle-outline", new ContextShortcutDefinition("-"), !atZero, "The volume is already zero."),
                ]),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Rename product…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(RemoveActionId, "Remove flow", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
            ];
        }

        if (model.GroupOf(elementId) is not null)
        {
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(RemoveActionId, "Remove group", "mdi-ungroup", new ContextShortcutDefinition("Delete")),
                ]),
                new ContextActionGroupDefinition([arrange]),
            ];
        }

        if (GestureIds.TryParsePlacement(elementId, out _, out _))
        {
            return
            [
                new ContextActionGroupDefinition(
                    [.. SupplyChainNodeTypes.All.Select(stage => new ContextActionDefinition("supply-chain.add." + stage, $"Add {SupplyChainNodeTypes.Display(stage).ToLowerInvariant()} here", IconOf(stage)))]),
                new ContextActionGroupDefinition([new ContextActionDefinition(AddGroupActionId, "Add group here", GroupIcon)]),
                new ContextActionGroupDefinition([arrange]),
            ];
        }

        return GestureIds.TryParseRelation(elementId, out _, out _)
            ? [new ContextActionGroupDefinition([new ContextActionDefinition(ConnectActionId, "Add flow", "mdi-arrow-right-thin")])]
            : [];
    }

    /// <summary>The property rows of <paramref name="elementId"/>, as <c>SupplyChainContextPropertyProvider.Describe</c> answered them.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(SupplyChainModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.NodeOf(elementId) is { } node)
        {
            var group = model.GroupOf(node.Group);
            return
            [
                new ContextPropertyDefinition("supply-chain.name", "Name", node.Name, Group: IdentityGroup),
                new ContextPropertyDefinition("supply-chain.stage", "Stage", node.Type, ContextPropertyEditor.Choice, Group: IdentityGroup, Candidates: SupplyChainNodeTypes.All),
                new ContextPropertyDefinition("supply-chain.group", "Group", group is null ? NoGroup : NameOf(group), ContextPropertyEditor.Choice, Group: IdentityGroup, Candidates: GroupChoices(model)),
                new ContextPropertyDefinition("supply-chain.description", "Description", node.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
                new ContextPropertyDefinition("supply-chain.quantity", "Quantity", DslNumber.Format(node.Quantity), Group: AmountGroup),
                new ContextPropertyDefinition("supply-chain.unit", "Unit", node.Unit, Group: AmountGroup),
                new ContextPropertyDefinition("supply-chain.step", "Step", DslNumber.Format(node.Step ?? SupplyChainGeometry.DefaultStep), Group: AmountGroup),
            ];
        }

        if (model.FlowOf(elementId) is { } flow)
        {
            return
            [
                new ContextPropertyDefinition("supply-chain.product", "Product", flow.Product, Group: IdentityGroup),
                new ContextPropertyDefinition("supply-chain.description", "Description", flow.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
                new ContextPropertyDefinition("supply-chain.volume", "Volume", DslNumber.Format(flow.Volume), Group: AmountGroup),
                new ContextPropertyDefinition("supply-chain.unit", "Unit", flow.Unit, Group: AmountGroup),
                new ContextPropertyDefinition("supply-chain.step", "Step", DslNumber.Format(flow.Step ?? SupplyChainGeometry.DefaultStep), Group: AmountGroup),
            ];
        }

        if (model.GroupOf(elementId) is { } frame)
        {
            return
            [
                new ContextPropertyDefinition("supply-chain.name", "Name", frame.Name, Group: IdentityGroup),
                new ContextPropertyDefinition("supply-chain.description", "Description", frame.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
            ];
        }

        return [];
    }

    /// <summary>What removing <paramref name="elementId"/> asked first, as <c>SupplyChainContextActionProvider.RunAsync</c> built it: a node with a flow to or from it.</summary>
    public static RemovalConfirmation? RemovalConfirmation(SupplyChainModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.NodeOf(elementId) is not { } node)
        {
            return null;
        }

        var going = model.Flows.Count(candidate => candidate.From == node.Id || candidate.To == node.Id);
        return going == 0
            ? null
            : new RemovalConfirmation(
                "Remove",
                going == 1
                    ? "Removing this node also removes the 1 flow to or from it."
                    : $"Removing this node also removes the {going} flows to or from it.",
                "Remove");
    }

    /// <summary>Everything wrong with <paramref name="model"/>, as <c>SupplyChainValidator.Validate</c> found it by hand.</summary>
    public static IReadOnlyList<SupplyChainBreach> Breaches(SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<SupplyChainBreach> breaches =
        [
            .. model.Problems.Select(problem => new SupplyChainBreach(SupplyChainRuleIds.UnreadableEntry, problem.Message, problem.Line, true)),
        ];

        var entries = model.Groups.Select(group => (group.Id, What: "group", group.Range.Start))
            .Concat(model.Nodes.Select(node => (node.Id, What: "node", node.Range.Start)))
            .Concat(model.Flows.Select(flow => (flow.Id, What: "flow", flow.Range.Start)))
            .ToList();

        breaches.AddRange(entries
            .Where(entry => entry.Id.Length == 0)
            .Select(entry => new SupplyChainBreach(SupplyChainRuleIds.MissingId, $"A {entry.What} has no id and is not drawn.", entry.Start, false)));

        breaches.AddRange(entries
            .Where(entry => entry.Id.Length > 0)
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Skip(1))
            .Select(entry => new SupplyChainBreach(SupplyChainRuleIds.DuplicateId, $"The id `{entry.Id}` is used more than once; only its first entry is drawn.", entry.Start, false)));

        var nodes = model.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var flow in model.Flows)
        {
            if (flow.From.Length > 0 && flow.From == flow.To)
            {
                breaches.Add(new SupplyChainBreach(SupplyChainRuleIds.SelfFlow, $"The flow `{flow.Id}` runs from `{flow.From}` to itself and is not drawn.", flow.Range.Start, false));
                continue;
            }

            foreach (var end in new[] { flow.From, flow.To }.Where(end => !nodes.Contains(end)))
            {
                breaches.Add(new SupplyChainBreach(SupplyChainRuleIds.DanglingFlow,
                    end.Length == 0 ? $"The flow `{flow.Id}` is missing an end and is not drawn." : $"The flow `{flow.Id}` names `{end}`, which is not a node here; it is not drawn.",
                    flow.Range.Start, false));
            }
        }

        var groups = model.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        breaches.AddRange(model.Nodes
            .Where(node => node.Group.Length > 0 && !groups.Contains(node.Group))
            .Select(node => new SupplyChainBreach(SupplyChainRuleIds.UnknownGroup, $"The node `{node.Id}` is in `{node.Group}`, which is not a group here; it is drawn outside every group.", node.Range.Start, true)));

        breaches.AddRange(model.Groups
            .Where(group => group.Id.Length > 0 && !group.IsPlaced && model.Nodes.All(node => node.Group != group.Id))
            .Select(group => new SupplyChainBreach(SupplyChainRuleIds.EmptyGroup, $"The group `{group.Id}` has no nodes and no `x` and `y`, so it is not drawn.", group.Range.Start, true)));

        return [.. breaches.OrderBy(breach => breach.Line)];
    }

    private static ToolboxItemDefinition Item(string type, string description) =>
        new($"supply-chain.toolbox.{type}", SupplyChainNodeTypes.Display(type), IconOf(type), description, "supply-chain.add." + type);

    private static string IconOf(string stage) => SupplyChainNodeTypes.Normalize(stage) switch
    {
        SupplyChainNodeTypes.Source => "mdi-tray-arrow-up",
        SupplyChainNodeTypes.Processor => "mdi-cog-outline",
        SupplyChainNodeTypes.Producer => "mdi-factory",
        SupplyChainNodeTypes.Integrator => "mdi-puzzle-outline",
        SupplyChainNodeTypes.Hub => "mdi-hub-outline",
        SupplyChainNodeTypes.Outlet => "mdi-storefront-outline",
        SupplyChainNodeTypes.Consumer => "mdi-account-outline",
        _ => "mdi-plus",
    };

    private static string NameOf(SupplyChainGroup group) => group.Name.Length > 0 ? group.Name : group.Id;

    private static IReadOnlyList<string> GroupChoices(SupplyChainModel model) =>
    [
        NoGroup,
        .. model.Groups.Where(group => group.Id.Length > 0).Select(NameOf).Distinct(StringComparer.Ordinal),
    ];
}
