using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Layout;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

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
/// <para>
/// <b>A move stays the host's to decide</b>, because DISL cannot state that a node dropped in a frame joins its group
/// (finding A13, gap 8): it builds one transaction of the definition's attributes, the position, the group the node
/// joins and the place of a group it leaves empty, and writes it as every other edit is written.
/// </para>
/// </remarks>
public sealed class MoveSupplyChainEntryCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<MoveSupplyChainEntryCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(SupplyChainBody document, SupplyChainModel model, MoveSupplyChainEntryCommand command)
    {
        var layout = SupplyChainLayout.Of(model);
        var diagram = SupplyChainDefinition.Diagram(model);
        if (layout.NodeBoxes.ContainsKey(command.EntryId))
        {
            return SupplyChainDefinition.Apply(document, model, MoveNode(diagram, layout, command));
        }

        return layout.GroupBoxes.TryGetValue(command.EntryId, out var frame)
            ? SupplyChainDefinition.Apply(document, model, MoveGroup(diagram, layout, frame, command))
            : EditOutcome.Refused("That is not something this diagram can move.");
    }

    private static DislTransaction MoveNode(DislDiagram diagram, SupplyChainLayout layout, MoveSupplyChainEntryCommand command)
    {
        var node = layout.Nodes.First(candidate => candidate.Id == command.EntryId);
        var current = layout.GroupBoxes.ContainsKey(node.Group) ? node.Group : "";
        var joins = GroupAt(layout, current, command.X + (SupplyChainGeometry.NodeWidth / 2), command.Y + (SupplyChainGeometry.NodeHeight / 2));

        List<DislChange> changes = [Placed(diagram, node.Id, "Stage", command.X, command.Y)];
        if (joins is not null)
        {
            changes.Add(new DislChange.Set(IdOf(diagram, node.Id, "Stage"), "Stage", new Dictionary<string, object?>(StringComparer.Ordinal) { ["group"] = IdOf(diagram, joins, "Group") }));
            if (current.Length > 0 && !layout.Nodes.Any(other => other.Id != node.Id && other.Group == current))
            {
                var frame = layout.GroupBoxes[current];
                changes.Add(Placed(diagram, current, "Group", frame.X, frame.Y));
            }
        }

        return new DislTransaction(changes, [], null);
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

    private static DislTransaction MoveGroup(DislDiagram diagram, SupplyChainLayout layout, Rect frame, MoveSupplyChainEntryCommand command)
    {
        var group = layout.Groups.First(candidate => candidate.Id == command.EntryId);
        (double dx, double dy) = (command.X - frame.X, command.Y - frame.Y);

        List<DislChange> changes =
        [
            .. layout.Nodes
                .Where(node => node.Group == group.Id)
                .DistinctBy(node => node.Id, StringComparer.Ordinal)
                .Select(node => Placed(diagram, node.Id, "Stage", layout.NodeBoxes[node.Id].X + dx, layout.NodeBoxes[node.Id].Y + dy)),
        ];

        // An empty group is placed where it was dropped; a placed group with members keeps its own
        // place the same distance from its frame, for when they go.
        if (changes.Count == 0 || group.IsPlaced)
        {
            (double x, double y) = changes.Count == 0 ? (command.X, command.Y) : (group.X!.Value + dx, group.Y!.Value + dy);
            changes.Add(Placed(diagram, group.Id, "Group", x, y));
        }

        return new DislTransaction(changes, [], null);
    }

    /// <summary>The entry written as <paramref name="id"/> placed at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private static DislChange.Set Placed(DislDiagram diagram, string id, string type, double x, double y) =>
        new(IdOf(diagram, id, type), type, SupplyChainDefinition.Position(x, y));

    /// <summary>The model id of the first entry of <paramref name="type"/> written as <paramref name="id"/>, as the layout draws it.</summary>
    private static string IdOf(DislDiagram diagram, string id, string type) =>
        diagram.NodesOfType(type).First(element => SupplyChainWritten.IdOf(element) == id).Id;
}
