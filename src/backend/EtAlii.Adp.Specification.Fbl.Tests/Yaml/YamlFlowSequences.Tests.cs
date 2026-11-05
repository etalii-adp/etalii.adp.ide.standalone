using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Yaml;

/// <summary>
/// FBL 0.10's yaml flow sequences (sections 4.3 and 6.2) for the cases the conformance fixtures do not reach: the items
/// of a flow sequence read as entries, the only item removed with and without its container, an insert after the last
/// of several items, and the refusals of a flow sequence over several lines and of a flow mapping.
/// </summary>
public class YamlFlowSequencesTests
{
    private static FblBinding Load(string remove)
    {
        var json = $$"""
            { "fbl": "0.10", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared",
              "elements": [{ "name": "tag", "type": "Tag", "at": "/groups/*/tags/*", "id": { "from": { "key": "name" } },
                "attributes": { "colour": { "key": "colour", "read": "text" } },
                "insert": { "place": "after-last", "container": "/groups/a/tags", "keys": ["name", "colour"] },
                "remove": { "container": "{{remove}}" } }] } } }
            """;
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return document!.Bindings["t"];
    }

    private static OpenBody Open(string body, string remove = "remove-when-empty") => OpenBody.Open(Encoding.UTF8.GetBytes(body), Load(remove));

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);

    [Fact]
    public void TheItemsOfAFlowSequence_AreReadAsEntries_WithTheirSlots()
    {
        // Act.
        var body = Open("groups:\n  a:\n    tags: [{name: x, colour: red}, {name: y}]\n");

        // Assert.
        Assert.Equal(["x", "y"], body.Model.Elements.Select(element => element.Id));
        Assert.Equal("red", body.Model.Find("x")!.Attributes["colour"]);
    }

    [Fact]
    public void TheOnlyItem_GoesWithItsContainer_UnderRemoveWhenEmpty_AndUndoPutsTheLineBack()
    {
        // Arrange.
        const string before = "groups:\n  a:\n    title: A\n    tags: [{name: x}]\n";
        var body = Open(before);

        // Act.
        var removed = body.Change(new ModelChange.Remove("x"));
        var written = Text(body);
        body.Undo();

        // Assert.
        Assert.IsType<PlanResult.Planned>(removed);
        Assert.Equal("groups:\n  a:\n    title: A\n", written);
        Assert.Equal(before, Text(body));
    }

    [Fact]
    public void TheOnlyItem_IsRemovedAlone_WhenItsContainerIsKept()
    {
        // Arrange.
        var body = Open("groups:\n  a:\n    tags: [{name: x}]\n", "keep");

        // Act.
        body.Change(new ModelChange.Remove("x"));

        // Assert.
        Assert.Equal("groups:\n  a:\n    tags: []\n", Text(body));
    }

    [Fact]
    public void AnItemBeforeAnother_IsRemovedWithTheSeparatorAfterIt()
    {
        // Arrange.
        var body = Open("groups:\n  a:\n    tags: [{name: x}, {name: y}]\n");

        // Act.
        body.Change(new ModelChange.Remove("x"));

        // Assert.
        Assert.Equal("groups:\n  a:\n    tags: [{name: y}]\n", Text(body));
    }

    [Fact]
    public void ANewItem_IsWrittenInFlowForm_AfterTheLastItem_QuotedWhereFlowNeedsIt()
    {
        // Arrange.
        var body = Open("groups:\n  a:\n    tags: [{name: x}]\n");

        // Act.
        var added = body.Change(new ModelChange.Add("Tag", "y, z", new Dictionary<string, object?> { ["colour"] = "red" }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(added);
        Assert.Equal("groups:\n  a:\n    tags: [{name: x}, {name: \"y, z\", colour: red}]\n", Text(body));
    }

    [Theory]
    [InlineData("groups:\n  a:\n    tags: [\n      {name: x}\n    ]\n")]
    [InlineData("groups:\n  a:\n    tags: {x: 1}\n")]
    public void AFlowSequenceOverSeveralLines_AndAFlowMapping_TakeNoInsert(string before)
    {
        // Arrange.
        var body = Open(before);

        // Act.
        var added = body.Change(new ModelChange.Add("Tag", "y", new Dictionary<string, object?>()));

        // Assert: refused with a reason, and nothing written.
        var refused = Assert.IsType<PlanResult.Refused>(added);
        Assert.False(string.IsNullOrWhiteSpace(refused.Reason));
        Assert.Equal(before, Text(body));
    }
}
