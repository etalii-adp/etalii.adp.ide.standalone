using System.Runtime.CompilerServices;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The DISL model of a parsed supply chain document, built from what <see cref="SupplyChainParser"/> read, with
/// hand-made <see cref="FblElement"/> records of the binding's types (<c>Group</c>, <c>Node</c>, <c>Flow</c>) through
/// <see cref="DislModelBuilder"/>, under <see cref="SupplyChainDefinition"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is the adapter until the module reads its file through its FBL binding</b> (supply-chain-disl-fbl,
/// tasks 10 to 12), which deletes it.
/// </para>
/// <para>
/// <b>Groups, nodes and flows share one id space, in that order.</b> The first entry written with an id is read
/// with it; an entry without an id, and a later holder of one, is read with an ephemeral id no written id can equal,
/// and marked <see cref="SupplyChainWritten.LaterHolder"/> when it is the second. Every entry keeps the id it is
/// written with as <see cref="SupplyChainWritten.StoredId"/>.
/// </para>
/// <para>
/// <b>A node's group is the reference only when a group has that id</b>, so <c>has(self.group)</c> means the node is
/// drawn inside one; the group as written is kept as <see cref="SupplyChainWritten.WrittenGroup"/>. A flow's end is the
/// first node written with its id, as the tool finds it, also where a group or an earlier node holds the id; an end that
/// names no node resolves to nothing. The ends as written are kept as <see cref="SupplyChainWritten.WrittenFrom"/> and
/// <see cref="SupplyChainWritten.WrittenTo"/>.
/// </para>
/// </remarks>
internal static class SupplyChainDisl
{
    /// <summary>What no written id can begin with: the start of an ephemeral id.</summary>
    private const char Ephemeral = '\u0001';

    /// <summary>What no read id begins with: the start of a flow end that names no node.</summary>
    private const char Nowhere = '\u0002';

    private static readonly ConditionalWeakTable<SupplyChainModel, DislModel> _models = new();

    /// <summary>The model of <paramref name="model"/>, built once per parsed document. Read it, never change it.</summary>
    public static DislModel Of(SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _models.GetValue(model, Build);
    }

    /// <summary>A model of <paramref name="model"/> of its own, for an edit to run over and change.</summary>
    public static DislModel Build(SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return DislModelBuilder.From(new FblModel(Elements(model), [], false), SupplyChainDefinition.Specification, new DislEnv());
    }

    private static List<FblElement> Elements(SupplyChainModel model)
    {
        var ids = new Ids();
        var groups = model.Groups.Where(group => group.Id.Length > 0).Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var elements = new List<FblElement>();
        foreach (var group in model.Groups)
        {
            elements.Add(Entry(ids, group.Id, "Group", false, group.Range, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = Present(group.Name),
                ["description"] = Present(group.Description),
                ["x"] = group.X,
                ["y"] = group.Y,
            }));
        }

        var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in model.Nodes)
        {
            var element = Entry(ids, node.Id, "Node", false, node.Range, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = Present(node.Type),
                ["name"] = Present(node.Name),
                ["description"] = Present(node.Description),
                ["group"] = groups.Contains(node.Group) ? node.Group : null,
                [SupplyChainWritten.WrittenGroup] = node.Group,
                ["quantity"] = node.Quantity,
                ["unit"] = Present(node.Unit),
                ["step"] = node.Step,
                ["x"] = node.X,
                ["y"] = node.Y,
            });
            nodes.TryAdd(node.Id, element.Id);
            elements.Add(element);
        }

        foreach (var flow in model.Flows)
        {
            elements.Add(Entry(ids, flow.Id, "Flow", true, flow.Range, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["from"] = EndOf(nodes, flow.From),
                ["to"] = EndOf(nodes, flow.To),
                [SupplyChainWritten.WrittenFrom] = flow.From,
                [SupplyChainWritten.WrittenTo] = flow.To,
                ["product"] = Present(flow.Product),
                ["description"] = Present(flow.Description),
                ["volume"] = flow.Volume,
                ["unit"] = Present(flow.Unit),
                ["step"] = flow.Step,
            }));
        }

        return elements;
    }

    private static FblElement Entry(Ids ids, string written, string type, bool isRelation, Documents.LineRange range, Dictionary<string, object?> attributes)
    {
        var stated = attributes.Where(attribute => attribute.Value is not null).ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
        stated[SupplyChainWritten.StoredId] = written;
        (string id, bool later) = ids.Next(written);
        if (later)
        {
            stated[SupplyChainWritten.LaterHolder] = true;
        }

        return new FblElement(id, written.Length > 0, type, type, isRelation, stated, null, null, null, null, default, range.Start + 1);
    }

    /// <summary>
    /// The end a flow written with <paramref name="written"/> reads: the first node written with that id, as the tool
    /// finds it, whichever entry holds the id first, so an end not written is the first node written without an id; an
    /// id no node is written with names nothing, and no end, with no such node, is absent.
    /// </summary>
    private static string? EndOf(Dictionary<string, string> nodes, string written) =>
        nodes.TryGetValue(written, out var read) ? read : written.Length == 0 ? null : $"{Nowhere}{written}";

    /// <summary>A value the parser read as empty text is not written: absent, as the file does not state it.</summary>
    private static string? Present(string value) => value.Length > 0 ? value : null;

    /// <summary>The ids handed out so far, across groups, nodes and flows.</summary>
    private sealed class Ids
    {
        private readonly HashSet<string> _held = new(StringComparer.Ordinal);
        private int _ephemeral;

        /// <summary>The id an entry written with <paramref name="written"/> is read with, and whether an earlier entry holds it.</summary>
        public (string Id, bool Later) Next(string written)
        {
            if (written.Length > 0 && _held.Add(written))
            {
                return (written, false);
            }

            var id = $"{Ephemeral}{_ephemeral++}{Ephemeral}{written}";
            return (id, written.Length > 0);
        }
    }
}
