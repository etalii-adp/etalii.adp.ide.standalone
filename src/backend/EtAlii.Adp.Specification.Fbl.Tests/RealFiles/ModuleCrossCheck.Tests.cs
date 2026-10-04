using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram.C4;
using EtAlii.Adp.Diagram.CausalLoopDiagram;
using EtAlii.Adp.Diagram.Mindmap;
using EtAlii.Adp.Diagram.Timeline;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.RealFiles;

/// <summary>
/// Requirement 11.6: for the four bindings whose module has a public parser, the ids per type the
/// binding reads from each real file equal the ids the module's own parser reads, mapped from the
/// module's kinds to the binding's types by <see cref="ModuleIds"/>. Every difference is a listed
/// divergence. Only public API of the modules is used, and nothing in them is changed.
/// </summary>
public partial class ModuleCrossCheckTests
{
    /// <summary>How many ids of a difference the observation names before it counts the rest.</summary>
    private const int Named = 5;

    public static TheoryData<string, string> Pairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in ModuleIds.Keys)
        {
            foreach (var file in RealFileCorpus.Files(RealFileCorpus.Find(key))) data.Add(key, file);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheBindingReadsTheIdsTheModuleReads(string key, string file)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(RealFileCorpus.FullPath(file));
        var binding = RealFileCorpus.Binding(RealFileCorpus.Find(key));
        var text = Encoding.UTF8.GetString(bytes);

        // Act.
        var body = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file));
        if (body.Model.Unreadable) return;
        var fbl = ModuleIds.OfBinding(key, body.Model);
        var module = ModuleIds.OfModule(key, text);

        // Assert.
        Divergences.Check("cross-check", key, file, Difference(fbl, module));
    }

    private static string? Difference(IReadOnlyDictionary<string, SortedSet<string>> fbl, IReadOnlyDictionary<string, SortedSet<string>> module)
    {
        var parts = new List<string>();
        foreach (var type in fbl.Keys.Union(module.Keys).Order(StringComparer.Ordinal))
        {
            var read = fbl.GetValueOrDefault(type) ?? [];
            var expected = module.GetValueOrDefault(type) ?? [];
            var lacks = expected.Except(read).ToList();
            var adds = read.Except(expected).ToList();
            if (lacks.Count == 0 && adds.Count == 0) continue;
            var part = new StringBuilder(type).Append(':');
            if (lacks.Count > 0) part.Append(" the binding lacks ").Append(List(lacks));
            if (adds.Count > 0) part.Append(lacks.Count > 0 ? ";" : "").Append(" the binding adds ").Append(List(adds));
            parts.Add(part.ToString());
        }
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string List(List<string> ids) =>
        $"{ids.Count} ({string.Join(", ", ids.Take(Named))}{(ids.Count > Named ? ", …" : "")})";
}

/// <summary>
/// The ids per type each side reads, made comparable: the type-mapping table of Requirement 11.6,
/// and the transforms the modules' own id schemes need.
/// </summary>
internal static partial class ModuleIds
{
    public static readonly IReadOnlyList<string> Keys = ["timeline", "causal-loop", "structurizr", "mindmap"];

    /// <summary>The C4 kinds the binding has rules for; the others (components, deployment) it does not read.</summary>
    private static readonly IReadOnlyDictionary<C4ElementKind, string> _c4Types = new Dictionary<C4ElementKind, string>
    {
        [C4ElementKind.Person] = "Person",
        [C4ElementKind.SoftwareSystem] = "SoftwareSystem",
        [C4ElementKind.Container] = "Container",
    };

    /// <summary>The ids C4Parser makes for an element without an identifier: kind, name slug, counter.</summary>
    [GeneratedRegex("^(person|softwaresystem|container)_[A-Za-z0-9]+_[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedC4Id();

    /// <summary>
    /// The binding's side: elements by type with their stored ids (an element without one is
    /// addressed by its place, which no module knows), and relations by their ends.
    /// </summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfBinding(string key, FblModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var element in model.Elements)
        {
            string? id = (key, element.IsRelation) switch
            {
                ("structurizr", true) => $"{element.Source}->{element.Target}",
                ("mindmap", true) => $"branch:{element.Source}->{element.Target}",
                (_, true) => element.Id,
                _ when !element.IdIsStored && key is not "causal-loop" => null,
                _ => element.Id,
            };
            if (id is null || id.Contains('@', StringComparison.Ordinal)) continue;
            Add(ids, element.Type, id);
        }
        return ids;
    }

    /// <summary>The module's side, through its own public parser.</summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfModule(string key, string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        switch (key)
        {
            case "timeline":
                var timeline = TimelineParser.Parse(LineDocument.Parse(text));
                foreach (var element in timeline.Elements.Where(e => e.Id.Length > 0)) Add(ids, element.IsPeriod ? "Period" : "Moment", element.Id);
                foreach (var connection in timeline.Connections.Where(c => c.Id.Length > 0)) Add(ids, "Connection", connection.Id);
                break;
            case "causal-loop":
                var loop = CausalLoopParser.Parse(CausalLoopDocument.Parse(text)).Model;
                foreach (var variable in loop.Variables) Add(ids, "Variable", variable.Id);
                foreach (var link in loop.Links) Add(ids, "CausalLink", link.Id);
                foreach (var cycle in loop.Loops) Add(ids, "Loop", cycle.Id);
                break;
            case "structurizr":
                var workspace = C4Parser.Parse(C4Document.Parse(text));
                foreach (var element in workspace.Elements)
                {
                    if (_c4Types.TryGetValue(element.Kind, out var type) && !GeneratedC4Id().IsMatch(element.Id)) Add(ids, type, element.Id);
                }
                foreach (var relationship in workspace.Relationships) Add(ids, "Relationship", $"{relationship.SourceId}->{relationship.DestinationId}");
                break;
            case "mindmap":
                var map = MindmapDocument.Parse(text, assignMissingIds: false);
                foreach (var node in map.Nodes.Where(n => n.Id.Length > 0))
                {
                    Add(ids, "Node", node.Id);
                    if (node.Parent is { Id.Length: > 0 } parent) Add(ids, "Branch", $"branch:{parent.Id}->{node.Id}");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "No module parser is cross-checked for this binding.");
        }
        return ids;
    }

    private static void Add(Dictionary<string, SortedSet<string>> ids, string type, string id)
    {
        if (!ids.TryGetValue(type, out var set))
        {
            set = new SortedSet<string>(StringComparer.Ordinal);
            ids[type] = set;
        }
        set.Add(id);
    }
}
