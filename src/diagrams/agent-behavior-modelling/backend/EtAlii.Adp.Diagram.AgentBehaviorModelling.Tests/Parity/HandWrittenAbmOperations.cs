using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The edits as the module's commands made them by hand before they ran through the DISL definition
/// (runtime plan step S19b): each one <see cref="ModelChange"/>, kept verbatim as the oracle the derived
/// ones are compared with (decision D6).
/// </summary>
internal static class HandWrittenAbmOperations
{
    /// <summary>A node of <paramref name="kind"/> added as <paramref name="parent"/>'s last child, with its starting label.</summary>
    public static AbmEdit AddChild(AbmBody document, AbmNode parent, string kind) =>
        document.Change(new ModelChange.Add(
            AbmEdits.TypeOf(kind), null, new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = AddAbmNodeCommandHandler.StartingLabel(kind) }, parent.Id, -1));

    /// <summary>A node of <paramref name="kind"/> added under <paramref name="parent"/> (null for the roots) before the child at <paramref name="index"/>.</summary>
    public static AbmEdit AddHere(AbmBody document, AbmNode? parent, int index, string kind) =>
        document.Change(new ModelChange.Add(
            AbmEdits.TypeOf(kind), null, new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = AddAbmNodeCommandHandler.StartingLabel(kind) }, parent?.Id, index));

    /// <summary>A node removed with its subtree.</summary>
    public static AbmEdit Remove(AbmBody document, AbmNode node) => document.Change(new ModelChange.Remove(node.Id));

    /// <summary>A node's kind changed to another.</summary>
    public static AbmEdit Retype(AbmBody document, AbmNode node, string kind) =>
        document.Change(new ModelChange.Retype(node.Id, AbmEdits.TypeOf(kind), new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["attempts"] = kind == AbmNodeKinds.Retry ? Math.Max(node.RetryCount, AbmNodeKinds.DefaultRetryCount) : 0,
        }));

    /// <summary>A node's notes replaced.</summary>
    public static AbmEdit Notes(AbmBody document, AbmNode node, string notes) =>
        document.Change(new ModelChange.Set(node.Id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["notes"] = notes }));

    /// <summary>A parent line drawn from <paramref name="parent"/> to <paramref name="child"/>.</summary>
    public static AbmEdit Connect(AbmBody document, AbmNode parent, AbmNode child) => document.Change(new ModelChange.Move(child.Id, parent.Id, -1));
}
