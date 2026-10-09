using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

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
/// </remarks>
public sealed class MoveSupplyChainEntryCommandHandler(ISupplyChainDocumentStore documents) : ICommandHandler<MoveSupplyChainEntryCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(MoveSupplyChainEntryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return SupplyChainEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            var layout = SupplyChainLayout.Of(model);
            if (layout.NodeBoxes.ContainsKey(command.EntryId))
            {
                return MoveNode(document, model, layout, command);
            }

            return layout.GroupBoxes.TryGetValue(command.EntryId, out var frame)
                ? MoveGroup(document, model, layout, frame, command)
                : SupplyChainEdit.Refused("That is not something this diagram can move.");
        });
    }

    private static SupplyChainEdit MoveNode(LineDocument document, SupplyChainModel model, SupplyChainLayout layout, MoveSupplyChainEntryCommand command)
    {
        var node = layout.Nodes.First(candidate => candidate.Id == command.EntryId);
        var current = layout.GroupBoxes.ContainsKey(node.Group) ? node.Group : "";
        var joins = GroupAt(layout, current, command.X + (SupplyChainGeometry.NodeWidth / 2), command.Y + (SupplyChainGeometry.NodeHeight / 2));

        SupplyChainWriter.Place(document, model, new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal) { [node.Id] = (command.X, command.Y) });
        if (joins is null)
        {
            return SupplyChainEdit.Applied;
        }

        // Every splice moves lines, so each next edit locates its entry in the document as it now is.
        var placed = SupplyChainParser.Parse(document);
        SupplyChainWriter.SetText(document, SupplyChainEdits.NodeOf(placed, node.Id)!.Range, SupplyChainKeys.Group, joins, removeWhenEmpty: false);

        var left = current.Length > 0 && !layout.Nodes.Any(other => other.Id != node.Id && other.Group == current);
        if (left)
        {
            var frame = layout.GroupBoxes[current];
            var regrouped = SupplyChainParser.Parse(document);
            SupplyChainWriter.PlaceGroup(document, SupplyChainEdits.GroupOf(regrouped, current)!, frame.X, frame.Y);
        }

        return SupplyChainEdit.Applied;
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

        static bool Holds(SupplyChainBox box, double x, double y) => x >= box.X && x <= box.Right && y >= box.Y && y <= box.Bottom;
    }

    private static SupplyChainEdit MoveGroup(LineDocument document, SupplyChainModel model, SupplyChainLayout layout, SupplyChainBox frame, MoveSupplyChainEntryCommand command)
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
            SupplyChainWriter.PlaceGroup(document, SupplyChainEdits.GroupOf(SupplyChainParser.Parse(document), group.Id)!, x, y);
        }

        return SupplyChainEdit.Applied;
    }
}
