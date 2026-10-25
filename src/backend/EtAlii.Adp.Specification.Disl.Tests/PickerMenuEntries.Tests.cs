using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A <c>connect</c> entry that chooses its other end from a list (DISL 0.3 §7.3, <c>target: "pick"</c>): its
/// <c>candidates</c> in the order the expression gives them, each with its <c>candidateLabel</c> (context
/// <c>element</c> plus <c>item</c>), the <c>pickerTitle</c>, and the entry unavailable with <c>emptyText</c> when
/// none remains.
/// </summary>
public class PickerMenuEntriesTests
{
    private const string Parts = """
        "metamodel": {
          "types": { "Thing": { "attributes": { "name": { "type": "string" } }, "labelAttribute": "name" } },
          "relations": { "Link": { "source": "Thing", "target": "Thing", "directed": true } }
        },
        "toolbox": { "contextMenus": [ { "for": ["Thing"], "tools": [
          { "kind": "connect", "via": "Link", "label": "Link to…", "target": "pick",
            "candidates": "diagram.nodesOfType('Thing').filter(n, n != self).sortBy(n, n.name)",
            "candidateLabel": { "cel": "'to ' + item.name" }, "pickerTitle": "Link to", "emptyText": "There is nothing else to link to." },
          { "kind": "connect", "via": "Link", "label": "Link", "target": "pick" } ] } ] }
        """;

    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With(Parts));

    private static IReadOnlyList<DerivedMenuEntry> Entries(DislDiagram diagram, string id) =>
        [.. ContextMenuDerivation.Derive(Specification, DislMenuTarget.Element(diagram.ElementById(id)!), new DislEnv(), WireIdMap.None).SelectMany(group => group.Entries)];

    private static DislDiagram Things(params string[] names)
    {
        var diagram = new DislDiagram(Specification);
        foreach (var name in names) diagram.AddNode("Thing", name, new Dictionary<string, object?> { ["name"] = name });
        return diagram;
    }

    [Fact]
    public void ThePicker_ListsTheCandidatesInTheirOrder_WithTheirLabelsAndTitle()
    {
        var entry = Entries(Things("c", "a", "b"), "a")[0];

        Assert.True(entry.Available);
        Assert.NotNull(entry.Picker);
        Assert.Equal("Link to", entry.Picker.Title);
        Assert.Equal(["b", "c"], entry.Picker.Candidates.Select(candidate => candidate.ElementId));
        Assert.Equal(["to b", "to c"], entry.Picker.Candidates.Select(candidate => candidate.Label));
    }

    [Fact]
    public void WithNoCandidate_TheEntryIsUnavailable_WithItsEmptyText()
    {
        var entry = Entries(Things("a"), "a")[0];

        Assert.False(entry.Available);
        Assert.Equal("There is nothing else to link to.", entry.UnavailableReason);
        Assert.Empty(entry.Picker!.Candidates);
    }

    [Fact]
    public void WithoutCandidatesOrLabels_EveryOtherAdmittedElementIsListed_ByItsLabel_UnderTheEntrysLabel()
    {
        var entry = Entries(Things("a", "b"), "a")[1];

        Assert.Equal("Link", entry.Picker!.Title);
        Assert.Equal(["b"], entry.Picker.Candidates.Select(candidate => candidate.ElementId));
        Assert.Equal(["b"], entry.Picker.Candidates.Select(candidate => candidate.Label));
    }

    [Fact]
    public void AnEntryThatDrags_HasNoPicker()
    {
        var specification = Specifications.Loaded(Specifications.With(Parts.Replace("\"target\": \"pick\" } ]", "\"target\": \"drag\" } ]", StringComparison.Ordinal)));
        var diagram = new DislDiagram(specification);
        diagram.AddNode("Thing", "a", new Dictionary<string, object?> { ["name"] = "a" });

        var entries = ContextMenuDerivation.Derive(specification, DislMenuTarget.Element(diagram.ElementById("a")!), new DislEnv(), WireIdMap.None).SelectMany(group => group.Entries).ToList();

        Assert.Null(entries[1].Picker);
    }
}
