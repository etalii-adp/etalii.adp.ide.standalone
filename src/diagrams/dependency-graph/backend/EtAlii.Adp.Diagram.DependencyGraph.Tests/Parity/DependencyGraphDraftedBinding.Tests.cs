using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity;

/// <summary>
/// Every finding the requirements mark Read, shown on its document of the parity corpus through the binding the
/// requirements draft (dependency-graph-disl-fbl, task 2, Requirements 1.2 and 1.4): what the module answers today,
/// what FBL answers through the drafted binding, and, beside them, the answer the finding asks for.
/// </summary>
/// <remarks>
/// <para>
/// <b>The drafted binding is a test resource</b> (<c>Parity/Drafted/dependency-graph.fbl</c>), the requirements'
/// appendix without its two keys that are not FBL (<c>"as": "text"</c>, <c>"unreadable": "default"</c>), so that it is
/// what FBL 0.2 can state today. The module does not read it.
/// </para>
/// <para>
/// <b>Each test is a pinned difference.</b> It was first written as an equality of the two answers and seen to fail
/// for the reason its finding gives (the failures are in the task's implementation log); it now pins both answers,
/// so that a change to either shows, and the task that repairs the finding turns it.
/// </para>
/// </remarks>
public class DependencyGraphDraftedBindingTests
{
    private static readonly Lazy<FblBinding> Drafted = new(() =>
    {
        var problems = FblDocumentLoader.Load(IoPath.Combine(AppContext.BaseDirectory, "Parity", "Drafted", "dependency-graph.fbl"), out var document);
        Assert.Empty(problems);
        return document!.Bindings["dgr"];
    });

    // ---- the findings -----------------------------------------------------------------------------

    [Fact]
    public void A4_TheSecondHolderOfAnId_IsAddressedByItsPlace()
    {
        // Arrange and act.
        var today = Today("inline/a4-id-held-twice").Elements.Select(element => element.Id);
        var fbl = Fbl("inline/a4-id-held-twice").Model;

        // Assert: today both holders answer to 'aaa', and a lookup by it finds the first; FBL gives the second its
        // place and reports it. The right answer is FBL's, with every lookup through the stored id (finding A4).
        Assert.Equal(["aaa", "bbb", "aaa"], today);
        Assert.Equal(["aaa", "bbb", "node@11"], fbl.Elements.Where(element => element.Type == "Node").Select(element => element.Id));
        Assert.Contains(fbl.Findings, finding => finding.Code == "std.duplicateId");
    }

    [Fact]
    public void B1_AnIdALabelAndAnEnd_AreTheTextTheyWereWrittenWith_OnlyToday()
    {
        // Arrange and act.
        var today = Today("inline/b1-scalars-as-written");
        var fbl = Fbl("inline/b1-scalars-as-written").Model.Elements;

        // Assert: today every scalar is its written text. FBL types it by the core schema: 007 and 7 are the one
        // number 7, 1.0 a number, true a boolean, ~ null, 0x1F 31. The right answer is today's (finding B1).
        Assert.Equal(["007", "7", "tilde"], today.Elements.Select(element => element.Id));
        Assert.Equal(["1.0", "true", "~"], today.Elements.Select(element => element.Label));
        Assert.Equal([7L, 7L], fbl.Where(element => element.Type == "Node").Take(2).Select(element => element.Attributes["storedId"]));
        Assert.Equal([1.0, true, null], fbl.Where(element => element.Type == "Node").Select(element => element.Attributes["label"]));
        Assert.Equal(31L, fbl.Single(element => element.Id == "hex-end").Attributes["writtenFrom"]);
    }

    [Fact]
    public void B2_ANumberThatWillNotRead_IsZeroToday_AndANumberOrTextToFbl()
    {
        // Arrange and act.
        var today = Today("inline/b2-numbers-read-otherwise").Elements.Select(element => element.X);
        var fbl = Fbl("inline/b2-numbers-read-otherwise").Model.Elements.Where(element => element.Type == "Node").Select(element => element.Attributes["x"]);

        // Assert: today 0x10 and .inf read as 0 and .NET's Infinity and NaN as numbers; FBL reads 16 and infinity, and
        // .NET's two spellings as text, with no finding either way. Which is right is Q4's (finding B2).
        Assert.Equal([0, 0, double.PositiveInfinity, double.NaN], today);
        Assert.Equal([16L, double.PositiveInfinity, "Infinity", "NaN"], fbl);
    }

    [Fact]
    public void B6_TheFirstNodeOfAGraph_KeepsTheCommentAfterTheEmptyList_OnlyThroughFbl()
    {
        // Arrange and act.
        var today = TodayAfter("inline/b6-empty-list-with-comment", (document, model) => DependencyGraphWriter.InsertElement(document, model, "newid", "New", 1, 2));
        var fbl = FblAfter("inline/b6-empty-list-with-comment", new ModelChange.Add("Node", "newid", Node("New", 1.0, 2L)));

        // Assert: today the list stays a flow list with an entry below it, and the body no longer reads; FBL opens it
        // into a block list and keeps the comment. The right answer is FBL's (finding B6).
        Assert.StartsWith("dependencies: 1\nelements: [] # nothing drawn yet\n  - id: newid\n", today.Text, StringComparison.Ordinal);
        Assert.False(today.Usable);
        Assert.StartsWith("dependencies: 1\nelements: # nothing drawn yet\n  - id: newid\n", fbl, StringComparison.Ordinal);
    }

    [Fact]
    public void B7_AChangedValue_KeepsItsQuotesItsCommentAndItsAnchor_OnlyThroughFbl()
    {
        // Arrange and act.
        var todayLabel = TodayAfter("inline/b7-value-styles", (document, model) => DependencyGraphWriter.SetLabel(document, model.Elements[0], "123"));
        var fblLabel = FblAfter("inline/b7-value-styles", new ModelChange.Set("aaa", new Dictionary<string, object?> { ["label"] = "123" }));
        var todayX = TodayAfter("inline/b7-value-styles", (document, model) => DependencyGraphWriter.SetX(document, model.Elements[0], 0.00001));
        var fblX = FblAfter("inline/b7-value-styles", new ModelChange.Set("aaa", new Dictionary<string, object?> { ["x"] = 0.00001 }));
        var todayBlock = TodayAfter("inline/b7-value-styles", (document, model) => DependencyGraphWriter.SetRelationLabel(document, model.Relations[0], "relabelled"));
        var fblBlock = FblAfter("inline/b7-value-styles", new ModelChange.Set("block", new Dictionary<string, object?> { ["label"] = "relabelled" }));

        // Assert: today the whole line is rewritten: the author's quotes and comment go, a label that reads as a
        // number is written plain, the anchor goes and the alias to it then names nothing, and a block scalar's lines
        // stay behind. FBL replaces the value in its own style. The right answer is FBL's, as Q1 rules (finding B7).
        Assert.Contains("    label: 123\n", todayLabel.Text, StringComparison.Ordinal);
        Assert.Contains("    label: '123' # kept by hand\n", fblLabel, StringComparison.Ordinal);
        Assert.Contains("    x: 0\n", todayX.Text, StringComparison.Ordinal);
        Assert.False(todayX.Usable);
        Assert.Contains("    x: &across 0.00001\n", fblX, StringComparison.Ordinal);
        Assert.EndsWith("    label: relabelled\n      first line\n      second line\n", todayBlock.Text, StringComparison.Ordinal);
        Assert.EndsWith("    label: relabelled\n", fblBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void B9_ANewEntry_CopiesTheFirstEntryToday_AndThePreviousSiblingThroughFbl()
    {
        // Arrange and act.
        var today = TodayAfter("inline/b9-entries-indented-apart", (document, model) => DependencyGraphWriter.InsertElement(document, model, "newid", "New", 1, 2));
        var fbl = FblAfter("inline/b9-entries-indented-apart", new ModelChange.Add("Node", "newid", Node("New", 1.0, 2L)));

        // Assert: the dash gap of one, the first entry's, against the gap of three, the previous sibling's (finding B9).
        Assert.Contains("  - id: newid\n    label: New\n", today.Text, StringComparison.Ordinal);
        Assert.Contains("  -   id: newid\n      label: New\n", fbl, StringComparison.Ordinal);
    }

    [Fact]
    public void B10_ARemoval_TakesTheCommentsDirectlyAboveIt_OnlyThroughFbl()
    {
        // Arrange and act.
        var today = TodayAfter("inline/b10-comments-above", (document, model) => DependencyGraphWriter.RemoveElement(document, model, model.Elements[1]));
        var fbl = FblAfter("inline/b10-comments-above", new ModelChange.Remove("bbb"));
        var todayLabel = TodayAfter("inline/b10-comments-above", (document, model) => DependencyGraphWriter.SetRelationLabel(document, model.Relations[0], ""));
        var fblLabel = FblAfter("inline/b10-comments-above", new ModelChange.Set("uses", new Dictionary<string, object?> { ["label"] = "" }));

        // Assert: today the comments above the removed node, its dependency and the cleared label stay behind; FBL
        // takes them with what they are about (FBL 4.1.1). Which is right is Q1's (finding B10).
        Assert.Contains("  # About bbb, directly above it.\n", today.Text, StringComparison.Ordinal);
        Assert.Contains("  # About the dependency.\n", today.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("# About", fbl, StringComparison.Ordinal);
        Assert.EndsWith("    to: bbb\n    # About its label.\n", todayLabel.Text, StringComparison.Ordinal);
        Assert.EndsWith("    to: bbb\n", fblLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void B11_RemovingTheSecondHolder_TakesTheFirstHoldersDependencies_TodayAndThroughFbl()
    {
        // Arrange and act.
        var today = TodayAfter("inline/b11-holders-and-an-anonymous-node", (document, model) => DependencyGraphWriter.RemoveElement(document, model, model.Elements[2]));
        var fbl = FblAfter("inline/b11-holders-and-an-anonymous-node", new ModelChange.Remove("node@11"));

        // Assert: both take every dependency that names 'aaa', although each resolves to the first holder: the module
        // compares the written id, and FBL's planner compares the reference's text (library item L2). The right answer
        // keeps both dependencies (finding B11).
        Assert.DoesNotContain("of-the-first", today.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("of-the-second", today.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("of-the-first", fbl, StringComparison.Ordinal);
        Assert.DoesNotContain("of-the-second", fbl, StringComparison.Ordinal);
    }

    [Fact]
    public void B11_RemovingANodeWithNoId_TakesTheDependenciesWithAnEndNotWritten_TodayAndThroughFbl()
    {
        // Arrange and act.
        var today = TodayAfter("inline/b11-holders-and-an-anonymous-node", (document, model) => DependencyGraphWriter.RemoveElement(document, model, model.Elements[3]));
        var fbl = FblAfter("inline/b11-holders-and-an-anonymous-node", new ModelChange.Remove("node@15"));

        // Assert: both take the dependency with no 'from' with the node that has no id: the module because an end not
        // written equals the node's missing id, FBL's planner because it compares the reference's text (library item
        // L2). The right answer keeps it, because it names nothing (finding B11).
        Assert.DoesNotContain("no-from", today.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("no-from", fbl, StringComparison.Ordinal);
        Assert.Contains("  - id: of-the-second\n", fbl, StringComparison.Ordinal);
    }

    [Fact]
    public void B14_AKeyOnTheDashLineWithAWideGap_IsFoundOnlyThroughFbl()
    {
        // Arrange and act.
        var today = TodayAfter("inline/b14-wide-dash-gap-and-nested-key", (document, model) => DependencyGraphWriter.SetLabel(document, model.Elements[0], "Parity: renamed"));
        var fbl = FblAfter("inline/b14-wide-dash-gap-and-nested-key", new ModelChange.Set("aaa", new Dictionary<string, object?> { ["label"] = "Parity: renamed" }));
        var todayNested = TodayAfter("inline/b14-wide-dash-gap-and-nested-key", (document, model) => DependencyGraphWriter.SetRelationLabel(document, model.Relations[0], "relabelled"));
        var fblNested = FblAfter("inline/b14-wide-dash-gap-and-nested-key", new ModelChange.Set("nested", new Dictionary<string, object?> { ["label"] = "relabelled" }));

        // Assert: today a second 'label' goes under '-   label: A' and the body no longer reads, and the nested
        // 'label' is changed instead of the entry's own; FBL changes the entry's own label in place. The right answer is
        // FBL's (finding B14).
        Assert.StartsWith("dependencies: 1\nelements:\n  -   label: A\n      label: \"Parity: renamed\"\n", today.Text, StringComparison.Ordinal);
        Assert.False(today.Usable);
        Assert.StartsWith("dependencies: 1\nelements:\n  -   label: \"Parity: renamed\"\n      id: aaa\n", fbl, StringComparison.Ordinal);
        Assert.EndsWith("    note:\n      label: relabelled\n    label: outer\n", todayNested.Text, StringComparison.Ordinal);
        Assert.EndsWith("    note:\n      label: inner\n    label: relabelled\n", fblNested, StringComparison.Ordinal);
    }

    [Fact]
    public void B16_AMergeKeyAndAFlowEntry_AreReadOtherwise()
    {
        // Arrange and act.
        var today = Today("inline/b16-merge-key-and-flow-entry");
        var fbl = Fbl("inline/b16-merge-key-and-flow-entry").Model.Elements.Where(element => element.Type == "Node").ToList();
        var todayMove = TodayAfter("inline/b16-merge-key-and-flow-entry", (document, model) => DependencyGraphWriter.SetX(document, model.Elements[1], 300));

        // Assert: today the merge key gives no row and the flow entry is a node with all its values, which the first
        // edit then breaks; FBL reads the merged row, and the flow entry as one value with no member it can read or
        // write, an entry with no id (finding B16).
        Assert.Equal([0, 1], today.Elements.Select(element => element.Row));
        Assert.Equal(["Row by merge", "Flow entry"], today.Elements.Select(element => element.Label));
        Assert.False(todayMove.Usable);
        Assert.Equal(3L, fbl[0].Attributes["row"]);
        Assert.Equal("node@9", fbl[1].Id);
        Assert.Equal("", fbl[1].Attributes["label"]);
    }

    [Fact]
    public void B17_AnXIsWrittenInAnotherForm()
    {
        // Arrange and act.
        var todaySmall = TodayAfter("inline/b17-x-written-small-and-large", (document, model) => DependencyGraphWriter.SetX(document, model.Elements[0], 0.00001));
        var fblSmall = FblAfter("inline/b17-x-written-small-and-large", new ModelChange.Set("aaa", new Dictionary<string, object?> { ["x"] = 0.00001 }));
        var todayLarge = TodayAfter("inline/b17-x-written-small-and-large", (document, model) => DependencyGraphWriter.SetX(document, model.Elements[0], 1e21));
        var fblLarge = FblAfter("inline/b17-x-written-small-and-large", new ModelChange.Set("aaa", new Dictionary<string, object?> { ["x"] = 1e21 }));

        // Assert: today the writer's form, which rounds 0.00001 to 0 and writes 1e21 in full, not .NET's 1E-05 and
        // 1E+21 as the finding reads; FBL's ECMAScript form. Which is right is Q1's (finding B17, whose wording is with
        // the owner).
        Assert.Contains("    x: 0\n", todaySmall.Text, StringComparison.Ordinal);
        Assert.Contains("    x: 0.00001\n", fblSmall, StringComparison.Ordinal);
        Assert.Contains("    x: 1000000000000000000000\n", todayLarge.Text, StringComparison.Ordinal);
        Assert.Contains("    x: 1e+21\n", fblLarge, StringComparison.Ordinal);
    }

    [Fact]
    public void Q2_ARepeatedKey_IsUnreadableToday_AndAWarningThroughFbl()
    {
        // Arrange and act.
        var today = DependencyGraphParser.Outcome(LineDocument.Parse(Text("inline/q2-repeated-key")));
        var fbl = Fbl("inline/q2-repeated-key");

        // Assert: the one change Q2 names, seen rather than taken on trust.
        Assert.False(today.IsUsable);
        Assert.False(fbl.IsReadOnly);
        Assert.Contains(fbl.Model.Findings, finding => finding.Code == "fbl.duplicate-key");
    }

    // ---- the binding's four open points -----------------------------------------------------------

    [Fact]
    public void TwoSlotsOnOneKey_ArePlannedAsOneWrite()
    {
        // Act: 'from' is a reference slot, and 'writtenFrom' a read-only slot on the same key.
        var fbl = FblAfter("inline/b17-x-written-small-and-large", new ModelChange.Set("between", new Dictionary<string, object?> { ["from"] = "bbb" }));

        // Assert.
        Assert.EndsWith("relations:\n  - id: between\n    from: bbb\n    to: bbb\n", fbl, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWhenOfTheRules_PassesAFlowMapping_WhichThenHasNoMemberToRead()
    {
        // Act.
        var fbl = Fbl("inline/b16-merge-key-and-flow-entry").Model.Elements;

        // Assert: the flow entries are read as a node and a dependency, each with no id and none of its values.
        Assert.Contains(fbl, element => element is { Id: "node@9", Type: "Node" });
        Assert.Contains(fbl, element => element is { Id: "dependency@11", Type: "Dependency" });
    }

    [Fact]
    public void RemovingTheLastDependency_KeepsTheRelationsKey()
    {
        // Act.
        var fbl = FblAfter("inline/b17-x-written-small-and-large", new ModelChange.Remove("between"));

        // Assert: 'remove.container' is 'keep', as the module keeps it today.
        Assert.EndsWith("    row: 1\nrelations:\n", fbl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("0.2")]
    [InlineData("0.5")]
    public async Task TheLoader_ReadsTheBindingAtEveryVersionItKnows(string version)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(IoPath.Combine(AppContext.BaseDirectory, "Parity", "Drafted", "dependency-graph.fbl"), TestContext.Current.CancellationToken);

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(text.Replace("\"fbl\": \"0.2\"", $"\"fbl\": \"{version}\"", StringComparison.Ordinal)), out _);

        // Assert: the draft declares 0.2; the loader accepts up to 0.5 without a word, and warns only above it.
        Assert.Empty(problems);
    }

    // ---- plumbing ---------------------------------------------------------------------------------

    private static string Text(string name) => DependencyGraphCorpus.Inline.Single(document => document.Name == name).Text;

    private static DependencyGraphModel Today(string name) => DependencyGraphParser.Parse(LineDocument.Parse(Text(name)));

    private static (string Text, bool Usable) TodayAfter(string name, Action<LineDocument, DependencyGraphModel> edit)
    {
        var document = LineDocument.Parse(Text(name));
        edit(document, DependencyGraphParser.Parse(document));
        return (document.Text, DependencyGraphParser.Outcome(LineDocument.Parse(document.Text)).IsUsable);
    }

    private static OpenBody Fbl(string name) => OpenBody.Open(Encoding.UTF8.GetBytes(Text(name)), Drafted.Value);

    private static string FblAfter(string name, ModelChange change)
    {
        var body = Fbl(name);
        Assert.IsType<PlanResult.Planned>(body.Change(change));
        return Encoding.UTF8.GetString(body.Bytes);
    }

    private static Dictionary<string, object?> Node(string label, double x, long row) => new() { ["label"] = label, ["x"] = x, ["row"] = row };
}
