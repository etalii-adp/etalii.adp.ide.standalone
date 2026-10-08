using EtAlii.Adp.Hierarchy;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// A behavior model as the library's elements: one element per node, of one of eleven types, and
/// one connection from every parent to each of its children.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is sent</b>: the type, the node's centre, and a payload carrying its keyword, label and
/// size. The notes are not sent; they are the agent's to read and the property grid's to edit.
/// </para>
/// <para>
/// <b>A connection's id is <c>child:</c> and its child's id</b>, because every node but a root has
/// exactly one parent: the id names the line without inventing a second identity for it, and can
/// never collide with a node's id, which holds only digits and dots.
/// </para>
/// <para>
/// <b>What breaks a rule is still drawn.</b> A leaf with children draws its children and the lines
/// to them, so the diagram shows the file the agent will actually read; the validator says why it
/// is wrong.
/// </para>
/// </remarks>
public sealed class AbmElementMapper
{
    private const string Prefix = "etalii/agent-behavior-modelling+";

    /// <summary>What a parent-to-child connection's id starts with.</summary>
    public const string ChildIdPrefix = "child:";

    /// <summary>The library type of a Do in order node.</summary>
    public const string SequenceType = Prefix + AbmNodeKinds.Sequence;

    /// <summary>The library type of a Try in order node.</summary>
    public const string FallbackType = Prefix + AbmNodeKinds.Fallback;

    /// <summary>The library type of a Do together node.</summary>
    public const string ParallelType = Prefix + AbmNodeKinds.Parallel;

    /// <summary>The library type of a Retry node.</summary>
    public const string RetryType = Prefix + AbmNodeKinds.Retry;

    /// <summary>The library type of a Repeat until node.</summary>
    public const string RepeatType = Prefix + AbmNodeKinds.Repeat;

    /// <summary>The library type of an Only while node.</summary>
    public const string GuardType = Prefix + AbmNodeKinds.Guard;

    /// <summary>The library type of an Ask approval before node.</summary>
    public const string ApprovalType = Prefix + AbmNodeKinds.Approval;

    /// <summary>The library type of a Check node.</summary>
    public const string CheckType = Prefix + AbmNodeKinds.Check;

    /// <summary>The library type of a Do node.</summary>
    public const string ActionType = Prefix + AbmNodeKinds.Action;

    /// <summary>The library type of an Ask the user node.</summary>
    public const string AskType = Prefix + AbmNodeKinds.Ask;

    /// <summary>The library type of a Delegate node.</summary>
    public const string DelegateType = Prefix + AbmNodeKinds.Delegate;

    /// <summary>The library type of a parent-to-child connection.</summary>
    public const string ChildType = Prefix + "child";

    /// <summary>The library type a node kind is drawn as.</summary>
    private static string ElementTypeOf(string kind) => Prefix + kind;

    /// <summary>The id of the connection that ends at <paramref name="childId"/>.</summary>
    public static string ConnectionIdOf(string childId) => ChildIdPrefix + childId;

    /// <summary>
    /// The elements a view of <paramref name="viewport"/> should hold: every node that overlaps it,
    /// and every connection with an end that does, together with both its ends.
    /// </summary>
    /// <param name="model">The tree.</param>
    /// <param name="stored">The positions the author dragged nodes to, by id; laid over the computed ones.</param>
    /// <param name="viewport">What the reader can see.</param>
    public IReadOnlyList<DiagramElement> Visible(AbmModel model, IReadOnlyDictionary<string, RegistrationPosition> stored, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(stored);

        // The whole tree is laid out first and culled afterwards, so a pan never re-packs it.
        var positions = AbmLayout.Arrange(model, stored);

        var shown = model.Nodes
            .Where(node => Overlaps(positions[node.Id], viewport))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        // A connection is kept on its ends, never on a position of its own, and brings both ends.
        var connections = model.Nodes
            .Where(node => node.ParentId is not null && (shown.Contains(node.Id) || shown.Contains(node.ParentId)))
            .ToList();
        foreach (var child in connections)
        {
            shown.Add(child.Id);
            shown.Add(child.ParentId!);
        }

        return
        [
            .. model.Nodes.Where(node => shown.Contains(node.Id)).Select(node => Node(node, positions[node.Id])),
            .. connections.Select(child => Connection(model, child)),
        ];
    }

    private static bool Overlaps(RegistrationPosition topLeft, DiagramViewport viewport) =>
        topLeft.X + AbmLayout.NodeWidth >= viewport.MinX
        && topLeft.X <= viewport.MaxX
        && topLeft.Y + AbmLayout.NodeHeight >= viewport.MinY
        && topLeft.Y <= viewport.MaxY;

    private static DiagramElement Node(AbmNode node, RegistrationPosition topLeft)
    {
        var payload = new AbmNodePayload
        {
            Keyword = node.Keyword,
            Label = node.Label,
            Width = AbmLayout.NodeWidth,
            Height = AbmLayout.NodeHeight,
            HasNotes = node.Notes.Length > 0,
            Implicit = !node.HasKeyword,
            Place = node.Id,
        };

        // The registration holds the top-left; the library draws from the centre.
        return Pack(
            node.Id,
            topLeft.X + (AbmLayout.NodeWidth / 2),
            topLeft.Y + (AbmLayout.NodeHeight / 2),
            ElementTypeOf(node.Kind),
            payload);
    }

    private static DiagramElement Connection(AbmModel model, AbmNode child)
    {
        var parent = model.NodeOf(child.ParentId!)!;
        var payload = new AbmChildPayload
        {
            FromElementId = parent.Id,
            ToElementId = child.Id,
            Index = parent.ChildIds.ToList().IndexOf(child.Id) + 1,
        };

        return Pack(ConnectionIdOf(child.Id), 0d, 0d, ChildType, payload);
    }

    private static DiagramElement Pack(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
