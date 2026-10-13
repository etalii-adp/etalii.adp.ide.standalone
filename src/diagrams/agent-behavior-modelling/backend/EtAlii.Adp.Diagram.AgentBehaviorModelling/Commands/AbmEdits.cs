using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>What the behavior model's command handlers share besides their base: the node types, a change of one attribute, and one refusal.</summary>
/// <remarks>
/// <para>
/// <b>Every edit is a <see cref="ModelChange"/></b> made on an
/// <see cref="AbmBody"/>: the persistence plugin plans it as the lines it touches, as
/// <see cref="AbmWriter"/> writes them.
/// </para>
/// </remarks>
internal static class AbmEdits
{
    /// <summary>The node type of <paramref name="kind"/>; a kind that is none of the eleven as itself, for the plugin to refuse in its words.</summary>
    public static string TypeOf(string kind) => AbmDefinition.TypeOfKind.GetValueOrDefault(kind, kind);

    /// <summary>The change that sets <paramref name="node"/>'s <paramref name="attribute"/> to <paramref name="value"/>.</summary>
    public static ModelChange Set(AbmNode node, string attribute, object? value) =>
        new ModelChange.Set(node.Id, new Dictionary<string, object?>(StringComparer.Ordinal) { [attribute] = value });

    /// <summary>The refusal for a node that is not there, in one wording.</summary>
    public static EditOutcome Gone() => EditOutcome.Refused("That node is no longer in this behavior model.");
}
