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
/// drawn inside one; the group as written is kept as <see cref="SupplyChainWritten.WrittenGroup"/>. A flow's ends are
/// written as read, and resolve to the first entry with that id.
/// </para>
/// </remarks>
internal static class SupplyChainDisl
{
    /// <summary>What no written id can begin with: the start of an ephemeral id.</summary>
    private const char Ephemeral = '\u0001';

    private static readonly ConditionalWeakTable<SupplyChainModel, DislModel> _models = new();

    /// <summary>The model of <paramref name="model"/>, built once per parsed document. Read it, never change it.</summary>
    public static DislModel Of(SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _models.GetValue(model, read => DislModelBuilder.From(new FblModel(Elements(read), [], false), SupplyChainDefinition.Specification, new DislEnv()));
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

        foreach (var node in model.Nodes)
        {
            elements.Add(Entry(ids, node.Id, "Node", false, node.Range, new Dictionary<string, object?>(StringComparer.Ordinal)
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
            }));
        }

        foreach (var flow in model.Flows)
        {
            elements.Add(Entry(ids, flow.Id, "Flow", true, flow.Range, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["from"] = Present(flow.From),
                ["to"] = Present(flow.To),
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
