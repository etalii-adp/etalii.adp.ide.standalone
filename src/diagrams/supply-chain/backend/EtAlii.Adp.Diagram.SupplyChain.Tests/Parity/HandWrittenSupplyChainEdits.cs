using System.Globalization;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// The command handlers as the module wrote them by hand before every edit was decided by the DISL definition
/// (supply-chain-disl-fbl, task 7), kept verbatim as the oracle the derived handlers are compared with, edit for edit:
/// the same bytes written, or the same refusal.
/// </summary>
internal static class HandWrittenSupplyChainEdits
{
    /// <summary>Adds a node where it was dropped, named after its stage.</summary>
    public sealed class AddSupplyChainNode(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<AddSupplyChainNodeCommand>(documents)
    {
        /// <inheritdoc />
        protected override async Task<CommandResult> RunAsync(AddSupplyChainNodeCommand command, CancellationToken cancellationToken)
        {
            return !SupplyChainNodeTypes.IsKnown(command.NodeType)
                ? CommandResult.Failure($"This notation has no `{command.NodeType}` stage.")
                : await base.RunAsync(command.NodeId.Length > 0 ? command : command with { NodeId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);
        }

        /// <inheritdoc />
        protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, AddSupplyChainNodeCommand command)
        {
            if (model.IsTaken(command.NodeId))
            {
                return EditOutcome.Refused("That id is already used in this diagram.");
            }

            // The drop is the centre; the document holds the top-left.
            var node = new SupplyChainNode(
                command.NodeId,
                SupplyChainNodeTypes.Normalize(command.NodeType),
                UniqueName.Of(model.Nodes.Select(existing => existing.Name), $"New {SupplyChainNodeTypes.Display(command.NodeType).ToLowerInvariant()}"),
                Description: "",
                Group: "",
                Quantity: 0,
                Unit: "",
                Step: null,
                X: command.X - (SupplyChainGeometry.NodeWidth / 2),
                Y: command.Y - (SupplyChainGeometry.NodeHeight / 2),
                Range: new LineRange(0, 0));

            return SupplyChainWriter.AddNode(document, model, node);
        }
    }

    /// <summary>Adds an empty group where it was dropped, placed so it draws before anything is moved into it.</summary>
    public sealed class AddSupplyChainGroup(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<AddSupplyChainGroupCommand>(documents)
    {
        /// <inheritdoc />
        protected override async Task<CommandResult> RunAsync(AddSupplyChainGroupCommand command, CancellationToken cancellationToken) =>
            await base.RunAsync(command.GroupId.Length > 0 ? command : command with { GroupId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);

        /// <inheritdoc />
        protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, AddSupplyChainGroupCommand command)
        {
            if (model.IsTaken(command.GroupId))
            {
                return EditOutcome.Refused("That id is already used in this diagram.");
            }

            // The drop is the centre; the document holds the top-left.
            var group = new SupplyChainGroup(
                command.GroupId,
                UniqueName.Of(model.Groups.Select(existing => existing.Name), "New group"),
                Description: "",
                new LineRange(0, 0),
                X: command.X - (SupplyChainGeometry.EmptyGroupWidth / 2),
                Y: command.Y - (SupplyChainGeometry.EmptyGroupHeight / 2));

            return SupplyChainWriter.AddGroup(document, model, group);
        }
    }

    /// <summary>Adds a flow between two nodes, refusing one to itself or one that already exists.</summary>
    public sealed class ConnectSupplyChainNodes(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<ConnectSupplyChainNodesCommand>(documents)
    {
        /// <inheritdoc />
        protected override async Task<CommandResult> RunAsync(ConnectSupplyChainNodesCommand command, CancellationToken cancellationToken) =>
            await base.RunAsync(command.FlowId.Length > 0 ? command : command with { FlowId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);

        /// <inheritdoc />
        protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, ConnectSupplyChainNodesCommand command)
        {
            if (model.NodeOf(command.From) is null || model.NodeOf(command.To) is null)
            {
                return EditOutcome.Refused("A flow runs from one node to another.");
            }

            if (command.From == command.To)
            {
                return EditOutcome.Refused("A node does not supply itself.");
            }

            if (model.Flows.Any(flow => flow.From == command.From && flow.To == command.To))
            {
                return EditOutcome.Refused("These two are already connected by a flow in this direction.");
            }

            if (model.IsTaken(command.FlowId))
            {
                return EditOutcome.Refused("That id is already used in this diagram.");
            }

            return SupplyChainWriter.AddFlow(document, model, new SupplyChainFlow(
                command.FlowId, command.From, command.To, Product: "", Description: "", Volume: null, Unit: "", Step: null, new LineRange(0, 0)));
        }
    }

    /// <summary>Removes whatever the id names.</summary>
    public sealed class RemoveSupplyChainEntry(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<RemoveSupplyChainEntryCommand>(documents)
    {
        /// <inheritdoc />
        protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, RemoveSupplyChainEntryCommand command)
        {
            if (model.NodeOf(command.EntryId) is { } node)
            {
                return SupplyChainWriter.RemoveNode(document, model, node);
            }

            if (model.FlowOf(command.EntryId) is { } flow)
            {
                return SupplyChainWriter.Remove(document, flow.Range);
            }

            return model.GroupOf(command.EntryId) is { } group
                ? SupplyChainWriter.RemoveGroup(document, model, group)
                : Gone;
        }
    }

    /// <summary>Checks the value against the key and the entry, then writes it.</summary>
    public sealed class SetSupplyChainProperty(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<SetSupplyChainPropertyCommand>(documents)
    {
        /// <inheritdoc />
        protected override async Task<CommandResult> RunAsync(SetSupplyChainPropertyCommand command, CancellationToken cancellationToken) =>
            Read(command, out _, out _) is { } refusal ? CommandResult.Failure(refusal) : await base.RunAsync(command, cancellationToken);

        /// <inheritdoc />
        protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, SetSupplyChainPropertyCommand command)
        {
            Read(command, out var value, out var number);
            if (model.NodeOf(command.EntryId) is { } node)
            {
                return command.Key switch
                {
                    SupplyChainKeys.Name => SupplyChainWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: false),
                    SupplyChainKeys.Description or SupplyChainKeys.Unit => SupplyChainWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: true),
                    SupplyChainKeys.Quantity or SupplyChainKeys.Step => SupplyChainWriter.SetNumber(document, node.Range, command.Key, number),
                    SupplyChainKeys.Type => SupplyChainNodeTypes.IsKnown(value)
                        ? SupplyChainWriter.SetText(document, node.Range, command.Key, SupplyChainNodeTypes.Normalize(value), removeWhenEmpty: false)
                        : EditOutcome.Refused($"`{value}` is not a stage: {string.Join(", ", SupplyChainNodeTypes.All)}."),
                    SupplyChainKeys.Group => value.Length == 0 || model.GroupOf(value) is not null
                        ? SupplyChainWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: true)
                        : EditOutcome.Refused($"There is no group `{value}` in this diagram."),
                    _ => Unknown(command.Key),
                };
            }

            if (model.FlowOf(command.EntryId) is { } flow)
            {
                return command.Key switch
                {
                    SupplyChainKeys.Product or SupplyChainKeys.Description or SupplyChainKeys.Unit => SupplyChainWriter.SetText(document, flow.Range, command.Key, value, removeWhenEmpty: true),
                    SupplyChainKeys.Volume or SupplyChainKeys.Step => SupplyChainWriter.SetNumber(document, flow.Range, command.Key, number),
                    _ => Unknown(command.Key),
                };
            }

            if (model.GroupOf(command.EntryId) is { } group)
            {
                return command.Key switch
                {
                    SupplyChainKeys.Name => SupplyChainWriter.SetText(document, group.Range, command.Key, value, removeWhenEmpty: false),
                    SupplyChainKeys.Description => SupplyChainWriter.SetText(document, group.Range, command.Key, value, removeWhenEmpty: true),
                    _ => Unknown(command.Key),
                };
            }

            return Gone;
        }

        /// <summary>The trimmed value and, for a number key, the number it reads as; or why it cannot be set.</summary>
        private static string? Read(SetSupplyChainPropertyCommand command, out string value, out double? number)
        {
            value = command.Value.Trim();
            number = null;
            if (SupplyChainKeys.Numbers.Contains(command.Key, StringComparer.Ordinal) && value.Length > 0)
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed < 0)
                {
                    return $"'{value}' is not an amount this diagram can hold: a number of zero or more.";
                }

                if (command.Key == SupplyChainKeys.Step && parsed == 0)
                {
                    return "A step of zero would make + and − do nothing.";
                }

                number = parsed;
            }

            return null;
        }

        private static EditOutcome Unknown(string key) => EditOutcome.Refused($"`{key}` cannot be set on this entry.");
    }

    /// <summary>
    /// The + and − of a node or a flow. The value never goes below zero: a decrement at zero is refused
    /// with a sentence rather than written as a negative stock nobody can have.
    /// </summary>
    public sealed class StepSupplyChainValue(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<StepSupplyChainValueCommand>(documents)
    {
        /// <inheritdoc />
        protected override async Task<CommandResult> RunAsync(StepSupplyChainValueCommand command, CancellationToken cancellationToken) =>
            command.Steps == 0 ? CommandResult.Failure("A step of nothing changes nothing.") : await base.RunAsync(command, cancellationToken);

        /// <inheritdoc />
        protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, StepSupplyChainValueCommand command)
        {
            if (model.NodeOf(command.EntryId) is { } node)
            {
                return Stepped(node.Quantity, node.Step, command.Steps, "quantity") is { } quantity
                    ? SupplyChainWriter.SetNumber(document, node.Range, SupplyChainKeys.Quantity, quantity)
                    : AtZero("quantity");
            }

            if (model.FlowOf(command.EntryId) is { } flow)
            {
                return Stepped(flow.Volume, flow.Step, command.Steps, "volume") is { } volume
                    ? SupplyChainWriter.SetNumber(document, flow.Range, SupplyChainKeys.Volume, volume)
                    : AtZero("volume");
            }

            return model.GroupOf(command.EntryId) is not null
                ? EditOutcome.Refused("A group has no value of its own; its members do.")
                : Gone;
        }

        /// <summary>The new value, or <c>null</c> when it is already zero and asked to go lower.</summary>
        private static double? Stepped(double? current, double? step, int steps, string what)
        {
            _ = what;
            var from = current ?? 0;
            if (from <= 0 && steps < 0)
            {
                return null;
            }

            // Rounded to the precision the writer keeps, so ten steps of 0.1 come back to a whole number.
            return Math.Max(0, Math.Round(from + (steps * (step is > 0 ? step.Value : SupplyChainGeometry.DefaultStep)), 4));
        }

        private static EditOutcome AtZero(string what) => EditOutcome.Refused($"The {what} is already zero.");
    }

    /// <summary>A new group around one node - the way a group comes into being, since an empty one draws nothing.</summary>
    public sealed class GroupSupplyChainNode(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<GroupSupplyChainNodeCommand>(documents)
    {
        /// <inheritdoc />
        protected override async Task<CommandResult> RunAsync(GroupSupplyChainNodeCommand command, CancellationToken cancellationToken)
        {
            return string.IsNullOrWhiteSpace(command.GroupName)
                ? CommandResult.Failure("A group needs a name.")
                : await base.RunAsync(command.GroupId.Length > 0 ? command : command with { GroupId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);
        }

        /// <inheritdoc />
        protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, GroupSupplyChainNodeCommand command)
        {
            if (model.NodeOf(command.NodeId) is not { } node)
            {
                return Gone;
            }

            if (model.IsTaken(command.GroupId))
            {
                return EditOutcome.Refused("That id is already used in this diagram.");
            }

            // The node's key first, then the group read afresh: adding the group moves every line
            // below the groups section, the node's among them.
            SupplyChainWriter.SetText(document, node.Range, SupplyChainKeys.Group, command.GroupId, removeWhenEmpty: false);
            var group = new SupplyChainGroup(command.GroupId, command.GroupName.Trim(), Description: "", new LineRange(0, 0));
            return SupplyChainWriter.AddGroup(document, SupplyChainParser.Parse(document), group);
        }
    }

    /// <summary>
    /// Moves a node, or a group with every member, in one edit and so in one undo - and moves a node
    /// into the group whose frame it is dropped in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A node dropped inside another group's frame joins that group.</b> Its centre decides, and the
    /// smallest frame wins where two overlap. Dropped anywhere else it stays in its own group, whose
    /// frame grows to follow it, so dragging a member outwards still widens its group.
    /// </para>
    /// <para>
    /// <b>A group a node leaves empty stays where it was</b>: its frame's top-left is written as its own
    /// <c>x</c> and <c>y</c>, which is where an empty group is drawn.
    /// </para>
    /// <para>
    /// <b>A group moves its members by the distance its frame moved</b>, and its own place with them,
    /// so it is still where it was left if its members later go.
    /// </para>
    /// </remarks>
    public sealed class MoveSupplyChainEntry(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<MoveSupplyChainEntryCommand>(documents)
    {
        /// <inheritdoc />
        protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, MoveSupplyChainEntryCommand command)
        {
            var layout = SupplyChainLayout.Of(model);
            if (layout.NodeBoxes.ContainsKey(command.EntryId))
            {
                return MoveNode(document, model, layout, command);
            }

            return layout.GroupBoxes.TryGetValue(command.EntryId, out var frame)
                ? MoveGroup(document, model, layout, frame, command)
                : EditOutcome.Refused("That is not something this diagram can move.");
        }

        private static EditOutcome MoveNode(LineDocument document, SupplyChainModel model, SupplyChainLayout layout, MoveSupplyChainEntryCommand command)
        {
            var node = layout.Nodes.First(candidate => candidate.Id == command.EntryId);
            var current = layout.GroupBoxes.ContainsKey(node.Group) ? node.Group : "";
            var joins = GroupAt(layout, current, command.X + (SupplyChainGeometry.NodeWidth / 2), command.Y + (SupplyChainGeometry.NodeHeight / 2));

            SupplyChainWriter.Place(document, model, new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal) { [node.Id] = (command.X, command.Y) });
            if (joins is null)
            {
                return EditOutcome.Applied;
            }

            // Every splice moves lines, so each next edit locates its entry in the document as it now is.
            var placed = SupplyChainParser.Parse(document);
            SupplyChainWriter.SetText(document, placed.NodeOf(node.Id)!.Range, SupplyChainKeys.Group, joins, removeWhenEmpty: false);

            var left = current.Length > 0 && !layout.Nodes.Any(other => other.Id != node.Id && other.Group == current);
            if (left)
            {
                var frame = layout.GroupBoxes[current];
                var regrouped = SupplyChainParser.Parse(document);
                SupplyChainWriter.PlaceGroup(document, regrouped.GroupOf(current)!, frame.X, frame.Y);
            }

            return EditOutcome.Applied;
        }

        /// <summary>
        /// The group a node whose centre is (<paramref name="x"/>, <paramref name="y"/>) is dropped into,
        /// or <c>null</c> when it stays where it is: inside its own frame, or inside no other.
        /// </summary>
        private static string? GroupAt(SupplyChainLayout layout, string current, double x, double y)
        {
            if (current.Length > 0 && Holds(layout.GroupBoxes[current], x, y))
            {
                return null;
            }

            return layout.Groups
                .Where(group => group.Id != current && Holds(layout.GroupBoxes[group.Id], x, y))
                .OrderBy(group => layout.GroupBoxes[group.Id].Width * layout.GroupBoxes[group.Id].Height)
                .Select(group => group.Id)
                .FirstOrDefault();
        }

        private static bool Holds(Rect box, double x, double y) => x >= box.X && x <= box.Right && y >= box.Y && y <= box.Bottom;

        private static EditOutcome MoveGroup(LineDocument document, SupplyChainModel model, SupplyChainLayout layout, Rect frame, MoveSupplyChainEntryCommand command)
        {
            var group = layout.Groups.First(candidate => candidate.Id == command.EntryId);
            (double dx, double dy) = (command.X - frame.X, command.Y - frame.Y);

            var places = layout.Nodes
                .Where(node => node.Group == group.Id)
                .ToDictionary(node => node.Id, node => (layout.NodeBoxes[node.Id].X + dx, layout.NodeBoxes[node.Id].Y + dy), StringComparer.Ordinal);

            if (places.Count > 0)
            {
                SupplyChainWriter.Place(document, model, places);
            }

            // An empty group is placed where it was dropped; a placed group with members keeps its own
            // place the same distance from its frame, for when they go.
            if (places.Count == 0 || group.IsPlaced)
            {
                (double x, double y) = places.Count == 0 ? (command.X, command.Y) : (group.X!.Value + dx, group.Y!.Value + dy);
                SupplyChainWriter.PlaceGroup(document, SupplyChainParser.Parse(document).GroupOf(group.Id)!, x, y);
            }

            return EditOutcome.Applied;
        }
    }

    /// <summary>Writes the placements, in one edit and so in one undo.</summary>
    public sealed class PlaceSupplyChainNodes(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<PlaceSupplyChainNodesCommand>(documents)
    {
        /// <inheritdoc />
        protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, PlaceSupplyChainNodesCommand command) =>
            SupplyChainWriter.Place(document, model, command.Places);
    }

    /// <summary>
    /// Writes the layered layout into the document: the positions <see cref="SupplyChainLayout"/>
    /// computes for every node, whatever the document stated before.
    /// </summary>
    /// <remarks>
    /// Written rather than cleared: a document whose nodes carry no coordinates is already drawn by
    /// this layout, so arranging it writes them all, and from then on dragging one moves only that one.
    /// </remarks>
    public sealed class ArrangeSupplyChain(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<ArrangeSupplyChainCommand>(documents)
    {
        /// <inheritdoc />
        protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, ArrangeSupplyChainCommand command)
        {
            var arranged = SupplyChainLayout.Of(model).Arranged;
            return arranged.Count == 0
                ? EditOutcome.Refused("There is nothing to arrange until this diagram has a node.")
                : SupplyChainWriter.Place(document, model, arranged);
        }
    }
}
