using System.Globalization;
using System.Runtime.CompilerServices;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The binding's reading of a <see cref="SupplyChainBody"/> as the module reads it: the <see cref="SupplyChainModel"/>
/// the layout, the mapper, the session and the commands read, every value turned from the text it was written with into
/// the module's value, and the sentences of what will not read (supply-chain-disl-fbl Requirements 4.2 to 4.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands in for what the languages cannot state</b>, each named in the definition's companion: FBL reads every
/// slot as its written text (gap 1, answered by FBL 0.5's <c>read: "text"</c>), but it cannot read a number that will
/// not convert without losing its entry, nor word a finding for an entry that is still drawn (an unknown key, a stage
/// or a number that will not read, the header's three sentences), so this class converts and reports. The binding
/// declares no <c>header</c>, so <c>fbl.header-mismatch</c> never appears.
/// </para>
/// <para>
/// <b>Numbers are read as the module always has</b> (finding B2): .NET's invariant parse of the text, quoted or not, so
/// <c>"50"</c> is 50, and <c>0x10</c> and <c>.inf</c> are not numbers and are reported. A stage is the binding's
/// <c>map</c> of the text, which reads the six older words as the stages they became and keeps any other as written.
/// </para>
/// <para>
/// <b>An entry written as a flow mapping</b> (<c>- {id: a, type: source}</c>) is one value to FBL, whose members CEL
/// sees but no key slot binds (FBL section 4.3), so its values are read from the binding's computed <c>written</c>
/// attribute: the written text of every member.
/// </para>
/// <para>
/// <b>The problems are listed in the order the module has always listed them</b>: each group entry (its unknown keys,
/// then its position), each node entry (its unknown keys, its stage, then its numbers), each flow entry (its unknown
/// keys, then its numbers), and the version last. <see cref="SupplyChainFindings"/> then orders the findings by line.
/// </para>
/// <para>
/// <b>Groups, nodes and flows share one id space, in that order.</b> The first entry written with an id is read with
/// it in the DISL model; an entry without an id, and a later holder of one, is read with an ephemeral id no written id
/// can equal, and marked <see cref="SupplyChainWritten.LaterHolder"/> when it is the second. Every entry keeps the id it
/// is written with as <see cref="SupplyChainWritten.StoredId"/>, a node's group and a flow's ends as written beside the
/// references, which resolve only to what is there.
/// </para>
/// <para>
/// <b>Reading never throws on content.</b> A body that cannot be read opens empty with the one problem it gives.
/// </para>
/// </remarks>
public static class SupplyChainReading
{
    /// <summary>How the sentence for a text that is not YAML begins; the reason follows it.</summary>
    private const string NotYaml = "The document could not be read as YAML: ";

    private const string DocumentRule = "document";
    private const string GroupRule = "group";
    private const string NodeRule = "node";
    private const string FlowRule = "flow";
    private const string HeaderKey = "supply-chain";
    private const string Written = "written";
    private const string UnknownKeys = "unknownKeys";

    /// <summary>What no written id can begin with: the start of an ephemeral id.</summary>
    private const char Ephemeral = '\u0001';

    /// <summary>What no read id begins with: the start of a flow end that names no node.</summary>
    private const char Nowhere = '\u0002';

    /// <summary>The rule of an entry that is not a mapping, by the rule of the list it stands in, and what the sentence calls it.</summary>
    private static readonly Dictionary<string, string> NotAMapping = new(StringComparer.Ordinal)
    {
        ["group-not-a-mapping"] = GroupRule,
        ["node-not-a-mapping"] = NodeRule,
        ["flow-not-a-mapping"] = FlowRule,
    };

    private static readonly ConditionalWeakTable<SupplyChainModel, SupplyChainBody> Bodies = [];

    /// <summary>What the store keeps of <paramref name="text"/>: the model, its problems and, for a body that cannot be read, why. <b>Never throws.</b></summary>
    public static ParseOutcome<SupplyChainModel> Outcome(string text) => SupplyChainBody.Parse(text).Reading;

    /// <summary>The model of <paramref name="text"/>. <b>Never throws.</b></summary>
    public static SupplyChainModel Parse(string text) => Outcome(text).Model;

    /// <summary>The body <paramref name="model"/> was read from; the empty body for a model no body was read into, such as <see cref="SupplyChainModel.Empty"/>.</summary>
    internal static SupplyChainBody BodyOf(SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Bodies.TryGetValue(model, out var body) ? body : SupplyChainBody.Parse("");
    }

    /// <summary>What <paramref name="body"/> reads: its model, every problem, and why it cannot be read when it cannot.</summary>
    internal static ParseOutcome<SupplyChainModel> Read(SupplyChainBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Refusal is { } refusal)
        {
            var problem = new ParseProblem(refusal.Line, NotYaml + refusal.Message);
            return new ParseOutcome<SupplyChainModel>(Remember(SupplyChainModel.Empty with { Problems = [problem] }, body), [problem], problem.Message);
        }

        var elements = body.Model.Elements.OrderBy(element => element.OwnSpan.Start).ToList();
        if (elements.FirstOrDefault(element => element.Rule == DocumentRule) is not { } document)
        {
            // A document whose root is not a mapping declares nothing, and says nothing about it.
            return new ParseOutcome<SupplyChainModel>(Remember(SupplyChainModel.Empty with { Problems = [] }, body), []);
        }

        List<ParseProblem> problems = [];
        List<SupplyChainGroup> groups = [];
        List<SupplyChainNode> nodes = [];
        List<SupplyChainFlow> flows = [];
        foreach (var rule in (ReadOnlySpan<string>)[GroupRule, NodeRule, FlowRule])
        {
            foreach (var element in elements)
            {
                if (element.Rule == rule)
                {
                    var range = body.LinesOf(element.OwnSpan);
                    ReportUnknownKeys(body, element, range, rule, problems);
                    switch (rule)
                    {
                        case GroupRule:
                            groups.Add(ReadGroup(body, element, range, problems));
                            break;
                        case NodeRule:
                            nodes.Add(ReadNode(body, element, range, problems));
                            break;
                        default:
                            flows.Add(ReadFlow(body, element, range, problems));
                            break;
                    }
                }
                else if (NotAMapping.GetValueOrDefault(element.Rule) == rule)
                {
                    problems.Add(new ParseProblem(body.LinesOf(element.OwnSpan).Start, $"A {rule} entry is not a mapping and was passed over."));
                }
            }
        }

        var version = ReadVersion(body, document, body.LinesOf(document.OwnSpan), problems);
        var model = Remember(new SupplyChainModel(groups, nodes, flows, problems, version), body);
        return new ParseOutcome<SupplyChainModel>(model, model.Problems);
    }

    /// <summary>
    /// The binding's reading of <paramref name="body"/> with every value as the module reads it, for
    /// <see cref="Specification.Disl.DislModelBuilder"/>: a number as a number, a value that will not read or is empty
    /// left out, a stage as the binding's <c>map</c> reads it, the ids as the module finds them, and what is written
    /// kept beside what resolves. The library's own findings and the entries that are not a mapping are left out: the
    /// definition's rules report what they would (Q4).
    /// </summary>
    internal static FblModel Values(SupplyChainBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new FblModel([.. Pairs(body).Select(pair => pair.Read)], [], false);
    }

    /// <summary>
    /// The entry the binding reads each group, node and flow as, by the id the DISL model reads it with: the ids differ for
    /// a later holder of an id and an entry without one, which the model reads with an ephemeral id and the binding by its
    /// place.
    /// </summary>
    internal static IReadOnlyDictionary<string, FblElement> BindingEntries(SupplyChainBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Pairs(body).ToDictionary(pair => pair.Read.Id, pair => pair.Entry, StringComparer.Ordinal);
    }

    /// <summary>Every group, node and flow the binding read, beside the element the DISL model reads it as.</summary>
    private static List<(FblElement Entry, FblElement Read)> Pairs(SupplyChainBody body)
    {
        var model = body.Reading.Model;
        var entries = body.Model.Elements.OrderBy(element => element.OwnSpan.Start).ToList();
        var groupEntries = new Queue<FblElement>(entries.Where(element => element.Rule == GroupRule));
        var nodeEntries = new Queue<FblElement>(entries.Where(element => element.Rule == NodeRule));
        var flowEntries = new Queue<FblElement>(entries.Where(element => element.Rule == FlowRule));

        var ids = new Ids();
        var groups = model.Groups.Where(group => group.Id.Length > 0).Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var elements = new List<(FblElement Entry, FblElement Read)>();
        foreach (var group in model.Groups)
        {
            elements.Add(Entry(ids, groupEntries.Dequeue(), group.Id, false, group.Range, new Dictionary<string, object?>(StringComparer.Ordinal)
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
            var element = Entry(ids, nodeEntries.Dequeue(), node.Id, false, node.Range, new Dictionary<string, object?>(StringComparer.Ordinal)
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
            nodes.TryAdd(node.Id, element.Read.Id);
            elements.Add(element);
        }

        foreach (var flow in model.Flows)
        {
            elements.Add(Entry(ids, flowEntries.Dequeue(), flow.Id, true, flow.Range, new Dictionary<string, object?>(StringComparer.Ordinal)
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

    /// <summary>
    /// <paramref name="entry"/> as the DISL model reads it: the id the module finds it by, the values that are set, the
    /// id as written, and whether an earlier entry holds that id.
    /// </summary>
    private static (FblElement Entry, FblElement Read) Entry(Ids ids, FblElement entry, string written, bool isRelation, LineRange range, Dictionary<string, object?> values)
    {
        var stated = values.Where(value => value.Value is not null).ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
        stated[SupplyChainWritten.StoredId] = written;
        (string id, bool later) = ids.Next(written);
        if (later)
        {
            stated[SupplyChainWritten.LaterHolder] = true;
        }

        return (entry, entry with { Id = id, IdIsStored = written.Length > 0, IsRelation = isRelation, Attributes = stated, Source = null, Target = null, Line = range.Start + 1 });
    }

    /// <summary>
    /// The end a flow written with <paramref name="written"/> reads: the first node written with that id, as the tool
    /// finds it, whichever entry holds the id first, so an end not written is the first node written without an id; an
    /// id no node is written with names nothing, and no end, with no such node, is absent.
    /// </summary>
    private static string? EndOf(Dictionary<string, string> nodes, string written) =>
        nodes.TryGetValue(written, out var read) ? read : written.Length == 0 ? null : $"{Nowhere}{written}";

    /// <summary>A value read as empty text is not written: absent, as the file does not state it.</summary>
    private static string? Present(string value) => value.Length > 0 ? value : null;

    private static SupplyChainModel Remember(SupplyChainModel model, SupplyChainBody body)
    {
        Bodies.AddOrUpdate(model, body);
        return model;
    }

    private static int? ReadVersion(SupplyChainBody body, FblElement document, LineRange root, List<ParseProblem> problems)
    {
        if (Scalar(document, "version", HeaderKey) is not { } text)
        {
            problems.Add(new ParseProblem(0, $"The document does not begin with `{HeaderKey}: {SupplyChainModel.CurrentVersion}`."));
            return null;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            problems.Add(new ParseProblem(body.KeyLine(root, HeaderKey), $"The version `{text}` is not a number."));
            return null;
        }

        if (version != SupplyChainModel.CurrentVersion)
        {
            problems.Add(new ParseProblem(body.KeyLine(root, HeaderKey), $"This document states version {version}; this module reads version {SupplyChainModel.CurrentVersion}."));
        }

        return version;
    }

    private static SupplyChainGroup ReadGroup(SupplyChainBody body, FblElement element, LineRange range, List<ParseProblem> problems) =>
        new(
            Scalar(element, SupplyChainWritten.StoredId, "id") ?? "",
            Scalar(element, "name", "name") ?? "",
            Scalar(element, "description", "description") ?? "",
            range,
            Number(body, element, "x", range, problems),
            Number(body, element, "y", range, problems));

    private static SupplyChainNode ReadNode(SupplyChainBody body, FblElement element, LineRange range, List<ParseProblem> problems)
    {
        var type = Scalar(element, "type", "type") is { } written ? SupplyChainNodeTypes.Normalize(written) : "";
        if (!SupplyChainNodeTypes.IsKnown(type))
        {
            problems.Add(new ParseProblem(range.Start, $"`{type}` is not a stage this module knows."));
        }

        return new SupplyChainNode(
            Scalar(element, SupplyChainWritten.StoredId, "id") ?? "",
            type,
            Scalar(element, "name", "name") ?? "",
            Scalar(element, "description", "description") ?? "",
            Scalar(element, "group", "group") ?? "",
            Number(body, element, "quantity", range, problems),
            Scalar(element, "unit", "unit") ?? "",
            Number(body, element, "step", range, problems),
            Number(body, element, "x", range, problems),
            Number(body, element, "y", range, problems),
            range);
    }

    private static SupplyChainFlow ReadFlow(SupplyChainBody body, FblElement element, LineRange range, List<ParseProblem> problems) =>
        new(
            Scalar(element, SupplyChainWritten.StoredId, "id") ?? "",
            Scalar(element, "from", "from") ?? "",
            Scalar(element, "to", "to") ?? "",
            Scalar(element, "product", "product") ?? "",
            Scalar(element, "description", "description") ?? "",
            Number(body, element, "volume", range, problems),
            Scalar(element, "unit", "unit") ?? "",
            Number(body, element, "step", range, problems),
            range);

    /// <summary>The keys an entry holds that the module does not read: the binding's computed <c>unknownKeys</c>, each reported on its own line and kept.</summary>
    private static void ReportUnknownKeys(SupplyChainBody body, FblElement element, LineRange range, string what, List<ParseProblem> problems)
    {
        if (element.Attributes.GetValueOrDefault(UnknownKeys) is not IEnumerable<object?> keys)
        {
            return;
        }

        foreach (var key in keys.OfType<string>())
        {
            problems.Add(new ParseProblem(body.KeyLine(range, key), $"`{key}` is not a key this module reads on a {what}; the line is kept."));
        }
    }

    /// <summary>
    /// A number from the entry, or null when the key is absent or will not read - the second of which is reported,
    /// because a silently missing value reads as a lost edit.
    /// </summary>
    private static double? Number(SupplyChainBody body, FblElement element, string key, LineRange range, List<ParseProblem> problems)
    {
        if (Scalar(element, key, key) is not { } text)
        {
            return null;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
        {
            return value;
        }

        problems.Add(new ParseProblem(body.KeyLine(range, key), $"`{key}: {text}` is not a number and was ignored."));
        return null;
    }

    /// <summary>
    /// The text <paramref name="attribute"/> is written with: its slot's, or, for an entry written as a flow mapping,
    /// its member's under <paramref name="key"/>; null when it is not written or is no scalar.
    /// </summary>
    private static string? Scalar(FblElement element, string attribute, string key)
    {
        if (element.Attributes.TryGetValue(attribute, out var value))
        {
            return value as string;
        }

        return element.Attributes.GetValueOrDefault(Written) is IReadOnlyDictionary<string, object?> members
            ? members.GetValueOrDefault(key) as string
            : null;
    }

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
