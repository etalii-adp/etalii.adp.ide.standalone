using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The persistence plugin writes exactly what <see cref="AbmWriter"/> writes (runtime plan step S17):
/// for every corpus document, in its own line endings, in LF, in CRLF, without its final newline and
/// with a byte order mark, and for every edit a node allows - rename, notes set and cleared, attempts,
/// an add of each kind at each place, a removal, a move to each parent and index, a change to each kind
/// - the bytes after <see cref="AbmBody.Change"/> are the bytes the writer gives on the same text, a
/// refusal is the writer's sentence, and reading the result again gives the parser's model.
/// </summary>
public class AbmPluginParityTests
{
    /// <summary>Three roots and nesting four deep, with notes, a blank line and a keyword-less item.</summary>
    private const string Forest =
        "# Agent\n\n## Behavior\n\n- **Do:** A\n  Note on A.\n- **Do in order:** B\n  - **Do:** B1\n\n    Two notes.\n  - **Try in order:** B2\n    - **Do:** B2a\n    - **Retry up to 2 times:** B2b\n      - **Ask the user:** B2b1\n  - **Check:** B3\n- plain item C\n\nAfter the tree.\n";

    private static readonly string[] Kinds = [.. AbmNodeKinds.All.Select(kind => kind.Id)];

    public static TheoryData<string, string> Documents()
    {
        var data = new TheoryData<string, string>();
        foreach ((string name, var _) in Texts())
        {
            foreach (var variant in (string[])["as-is", "lf", "crlf", "no-final-newline", "bom"]) data.Add(name, variant);
        }
        return data;
    }

    private static IEnumerable<(string Name, string Text)> Texts() => AbmDisl.Corpus().Append(("inline/forest", Forest));

    private static byte[] Variant(string name, string variant)
    {
        var text = Texts().Single(document => document.Name == name).Text;
        var lf = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return variant switch
        {
            "lf" => Encoding.UTF8.GetBytes(lf),
            "crlf" => Encoding.UTF8.GetBytes(lf.Replace("\n", "\r\n", StringComparison.Ordinal)),
            "no-final-newline" => Encoding.UTF8.GetBytes(text.TrimEnd('\r', '\n')),
            "bom" => [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)],
            _ => Encoding.UTF8.GetBytes(text),
        };
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryEdit_WritesTheWritersBytes_RefusesInItsWords_AndRereadsAsTheParserReads(string name, string variant)
    {
        // Arrange.
        var bytes = Variant(name, variant);
        var model = Writer(bytes, (_, _) => AbmEdit.Applied).Model;
        var differences = new List<string>();
        var count = 0;

        // Act.
        foreach ((string label, ModelChange change, Func<LineDocument, AbmModel, AbmEdit> edit) in Edits(model))
        {
            count++;
            var expected = Writer(bytes, edit);
            var body = AbmBody.Open(bytes);
            var outcome = body.Change(change);
            if (outcome.Refusal != expected.Edit.Refusal) differences.Add($"{label}: refused '{outcome.Refusal}' where the writer said '{expected.Edit.Refusal}'");
            else if (!body.Bytes.AsSpan().SequenceEqual(expected.Bytes)) differences.Add($"{label}: bytes differ\n{Encoding.UTF8.GetString(body.Bytes)}\n---\n{Encoding.UTF8.GetString(expected.Bytes)}");
            else if (Reread(body) != Parsed(expected.Bytes)) differences.Add($"{label}: the reading differs from the parser's");
        }

        // Assert.
        Assert.True(count > 20, $"Only {count} edits were tried.");
        Assert.True(differences.Count == 0, $"{differences.Count} of {count} edits differ:\n{string.Join("\n", differences.Take(5))}");
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void TheReading_IsTheParsersModel_AndTheDefinitionDerivesItsIds(string name, string variant)
    {
        // Arrange.
        var bytes = Variant(name, variant);
        var model = AbmParser.Parse(LineDocument.Parse(Encoding.UTF8.GetString(bytes)));

        // Act.
        var body = AbmBody.Open(bytes);
        var disl = body.Disl;

        // Assert.
        Assert.Equal(Parsed(bytes), Reread(body));
        Assert.All(body.Reading.Elements, element => Assert.False(element.IdIsStored));
        Assert.DoesNotContain(disl.Findings, finding => finding.Code.StartsWith("std.", StringComparison.Ordinal));
        Assert.Equal(model.Nodes.Select(node => node.Id), disl.Diagram.Nodes.Select(node => node.Id));
        Assert.Equal(model.Nodes.Select(node => node.ParentId), disl.Diagram.Nodes.Select(node => node.Parent?.Id));
    }

    /// <summary>The examples are in the corpus as their Markdown, not as their paths.</summary>
    [Fact]
    public void EveryExample_IsATreeOfItsOwn()
    {
        // Act.
        var sizes = AbmDisl.Corpus().Where(document => document.Name.StartsWith("examples/", StringComparison.Ordinal)).Select(document => AbmDisl.Parse(document.Text).Nodes.Count).ToList();

        // Assert.
        Assert.Equal(AbmExamples.Names.Count, sizes.Count);
        Assert.All(sizes, size => Assert.True(size >= 10, $"An example reads {size} nodes."));
    }

    [Fact]
    public void AnElementsSpan_IsItsSubtreesBytes()
    {
        // Arrange.
        var bytes = Encoding.UTF8.GetBytes(Forest.Replace("\n", "\r\n", StringComparison.Ordinal));

        // Act.
        var reading = AbmBody.Open(bytes).Reading;

        // Assert.
        var b2 = reading.Elements.Single(element => element.Id == "2.2");
        Assert.Equal("  - **Try in order:** B2\r\n    - **Do:** B2a\r\n    - **Retry up to 2 times:** B2b\r\n      - **Ask the user:** B2b1\r\n", Encoding.UTF8.GetString(bytes, b2.OwnSpan.Start, b2.OwnSpan.End - b2.OwnSpan.Start));
        Assert.Equal(11, b2.Line);
        Assert.Equal(("Fallback", "2", "children"), (b2.Type, b2.ParentId, b2.ParentSlot));
    }

    [Fact]
    public void AMove_IsPlannedAsTheSubtreeTakenOutAndPutInAgain()
    {
        // Arrange.
        var bytes = Encoding.UTF8.GetBytes(Forest);
        var plugin = new AbmMarkdownPlugin();
        var last = plugin.Read(new PluginReadRequest([new PluginFile("", bytes)]));

        // Act.
        var plan = plugin.Plan(new PluginPlanRequest([new PluginFile("", bytes)], last, new ModelChange.Move("2.2", null, 0)));

        // Assert.
        var planned = Assert.IsType<PluginPlanResult.Planned>(plan);
        Assert.Equal([SpliceOperation.InsertEntry, SpliceOperation.RemoveEntry], planned.Splices.Select(splice => splice.Splice.Operation));
    }

    [Fact]
    public void ANoBehaviorFile_IsReportedOnItsFirstLine_AndAnEmptySectionOnItsHeading()
    {
        // Act.
        var none = AbmBody.Parse("# Notes\n\nNothing here.\n").Reading.Findings;
        var empty = AbmBody.Parse("# Agent\n\n## Behavior\n\nNo list yet.\n").Reading.Findings;

        // Assert.
        Assert.Equal("abm.no-behavior Info 1", string.Join(";", none.Select(finding => $"{finding.Code} {finding.Severity} {finding.Location!.Line}")));
        Assert.Equal("abm.no-behavior Info 3", string.Join(";", empty.Select(finding => $"{finding.Code} {finding.Severity} {finding.Location!.Line}")));
    }

    /// <summary>Every edit a node allows, as the change the plugin is given and the writer call it stands for.</summary>
    private static IEnumerable<(string Label, ModelChange Change, Func<LineDocument, AbmModel, AbmEdit> Edit)> Edits(AbmModel model)
    {
        var parents = model.Nodes.Select(node => (AbmNode?)node).Prepend(null).ToList();
        foreach (var node in model.Nodes)
        {
            var id = node.Id;
            yield return ($"rename {id}", Set(id, "label", "Renamed ü\nwith a break"), (d, m) => AbmWriter.SetLabel(d, m.NodeOf(id)!, "Renamed ü\nwith a break"));
            yield return ($"notes {id}", Set(id, "notes", "First\n\n  Second"), (d, m) => AbmWriter.SetNotes(d, m.NodeOf(id)!, "First\n\n  Second"));
            yield return ($"clear notes {id}", Set(id, "notes", ""), (d, m) => AbmWriter.SetNotes(d, m.NodeOf(id)!, ""));
            if (node.Kind == AbmNodeKinds.Retry)
            {
                yield return ($"attempts {id}", Set(id, "attempts", 5L), (d, m) => AbmWriter.SetKind(d, m.NodeOf(id)!, AbmNodeKinds.Retry, 5));
            }
            yield return ($"remove {id}", new ModelChange.Remove(id), (d, m) => AbmWriter.Remove(d, m.NodeOf(id)!));
            foreach (var kind in Kinds)
            {
                var attempts = kind == AbmNodeKinds.Retry ? 3 : 0;
                yield return ($"retype {id} {kind}", new ModelChange.Retype(id, AbmDisl.TypeOfKind[kind], Attempts(attempts)), (d, m) => AbmWriter.SetKind(d, m.NodeOf(id)!, kind, attempts));
            }
            yield return ($"retype {id} retry 0", new ModelChange.Retype(id, "Retry", Attempts(0)), (d, m) => AbmWriter.SetKind(d, m.NodeOf(id)!, AbmNodeKinds.Retry, 0));
            foreach (var parent in parents)
            {
                var parentId = parent?.Id;
                var siblings = parent is null ? model.Roots.Count : parent.ChildIds.Count;
                for (var index = -1; index <= siblings + 1; index++)
                {
                    var at = index;
                    yield return ($"move {id} to {parentId ?? "root"} at {at}", new ModelChange.Move(id, parentId, at),
                        (d, m) => AbmWriter.Move(d, m, m.NodeOf(id)!, parentId is null ? null : m.NodeOf(parentId), at));
                }
            }
        }

        foreach (var parent in parents)
        {
            var parentId = parent?.Id;
            var siblings = parent is null ? model.Roots.Count : parent.ChildIds.Count;
            for (var index = -1; index <= siblings + 1; index++)
            {
                foreach (var kind in index is -1 or 0 ? Kinds : [AbmNodeKinds.Action])
                {
                    var at = index;
                    var attributes = new Dictionary<string, object?> { ["label"] = "New " + kind };
                    yield return ($"add {kind} under {parentId ?? "root"} at {at}", new ModelChange.Add(AbmDisl.TypeOfKind[kind], null, attributes, parentId, at),
                        (d, m) => AbmWriter.Add(d, m, parentId is null ? null : m.NodeOf(parentId), at, kind, "New " + kind).Edit);
                }
            }
        }

        yield return ("add gone", new ModelChange.Add("Do", null, new Dictionary<string, object?>(), "9.9"), (_, _) => AbmEdit.Refused("That node is no longer in this behavior model."));
        yield return ("remove gone", new ModelChange.Remove("9.9"), (_, _) => AbmEdit.Refused("That node is no longer in this behavior model."));
    }

    private static ModelChange.Set Set(string id, string name, object? value) => new(id, new Dictionary<string, object?> { [name] = value });

    private static Dictionary<string, object?> Attempts(int attempts) => new() { ["attempts"] = (long)attempts };

    /// <summary>Today's path: the text decoded, the writer's edit on its lines, and their bytes.</summary>
    private static (AbmEdit Edit, byte[] Bytes, AbmModel Model) Writer(byte[] bytes, Func<LineDocument, AbmModel, AbmEdit> edit)
    {
        var document = LineDocument.Parse(Encoding.UTF8.GetString(bytes));
        var model = AbmParser.Parse(document);
        var outcome = edit(document, model);
        return (outcome, outcome.WasApplied ? Encoding.UTF8.GetBytes(document.Text) : bytes, model);
    }

    /// <summary>The parser's model of <paramref name="bytes"/> as the plugin should read it, one line per node.</summary>
    private static string Parsed(byte[] bytes) => string.Join("\n", AbmParser.Parse(LineDocument.Parse(Encoding.UTF8.GetString(bytes))).Nodes.Select(node =>
        $"{node.Id} {AbmDisl.TypeOfKind[node.Kind]} {node.ParentId} line {node.Line + 1} label={node.Label} notes={node.Notes}"
        + (node.Kind == AbmNodeKinds.Retry ? $" attempts={node.RetryCount}" : "")
        + (node.Kind == AbmNodeKinds.Action ? $" implicit={!node.HasKeyword}" : "")));

    /// <summary>What the plugin reads from the body, in the same shape.</summary>
    private static string Reread(AbmBody body) => string.Join("\n", body.Reading.Elements.Select(element =>
        $"{element.Id} {element.Type} {element.ParentId} line {element.Line} label={element.Attributes["label"]} notes={element.Attributes["notes"]}"
        + (element.Attributes.TryGetValue("attempts", out var attempts) ? $" attempts={attempts}" : "")
        + (element.Attributes.TryGetValue("implicit", out var flag) ? $" implicit={flag}" : "")));
}
