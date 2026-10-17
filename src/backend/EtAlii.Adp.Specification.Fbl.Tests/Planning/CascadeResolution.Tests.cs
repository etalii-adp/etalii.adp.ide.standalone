using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Planning;

/// <summary>
/// A cascade takes the entries whose reference names the removed entry (FBL 6.2), and a reference names the entry it
/// resolves to: the first holder of a stored id, since a later holder is addressed by its place (FBL 5.3, 5.4). An
/// entry whose reference only repeats the removed entry's written key, or whose reference is not written at all, stays.
/// </summary>
public class CascadeResolutionTests
{
    private static readonly FblBinding _binding = Inline("""
        [{
          "name": "item", "type": "Item", "at": "/items/*",
          "id": { "from": { "key": "id" } },
          "attributes": { "label": { "key": "label" } },
          "remove": { "cascade": ["task"] }
        },
        {
          "name": "task", "type": "Task", "at": "/tasks/*",
          "id": { "from": { "key": "id" } },
          "attributes": { "owner": { "key": "owner", "reference": { "to": ["item"], "by": "id" } } },
          "remove": {}
        }]
        """);

    /// <summary>The same, with the reference by an attribute that reads the id's key, as a binding does that keeps the written id as a host attribute.</summary>
    private static readonly FblBinding _byAttribute = Inline("""
        [{
          "name": "item", "type": "Item", "at": "/items/*",
          "id": { "from": { "key": "id" } },
          "attributes": { "code": { "key": "id", "readOnly": true }, "label": { "key": "label" } },
          "remove": { "cascade": ["task"] }
        },
        {
          "name": "task", "type": "Task", "at": "/tasks/*",
          "id": { "from": { "key": "id" } },
          "attributes": { "owner": { "key": "owner", "reference": { "to": ["item"], "by": "code" } } },
          "remove": {}
        }]
        """);

    private const string Body = """
        items:
          - id: a
            label: First holder
          - id: b
            label: Other
          - id: a
            label: Second holder
          - label: Has no id
        tasks:
          - id: of-the-first
            owner: a
          - id: of-the-other
            owner: b
          - id: owned-by-nobody
        """;

    public static TheoryData<string> Bindings => new("by id", "by an attribute");

    [Theory]
    [MemberData(nameof(Bindings))]
    public void RemovingTheSecondHolderOfAnId_TakesNoTaskThatNamesTheFirst(string by)
    {
        // Arrange.
        var body = Open(by);
        var second = body.Model.Elements.Single(element => element is { Type: "Item", IdIsStored: false } && Equals(element.Attributes["label"], "Second holder"));

        // Act.
        Assert.IsType<PlanResult.Planned>(body.Change(new ModelChange.Remove(second.Id)));

        // Assert: 'a' names the first holder, so its task stays.
        Assert.Equal(["of-the-first", "of-the-other", "owned-by-nobody"], Tasks(body));
    }

    [Theory]
    [MemberData(nameof(Bindings))]
    public void RemovingTheFirstHolderOfAnId_TakesTheTaskThatNamesIt(string by)
    {
        // Arrange.
        var body = Open(by);

        // Act.
        Assert.IsType<PlanResult.Planned>(body.Change(new ModelChange.Remove("a")));

        // Assert.
        Assert.Equal(["of-the-other", "owned-by-nobody"], Tasks(body));
    }

    [Theory]
    [MemberData(nameof(Bindings))]
    public void RemovingAnItemWithNoId_TakesNoTaskWhoseReferenceIsNotWritten(string by)
    {
        // Arrange.
        var body = Open(by);
        var anonymous = body.Model.Elements.Single(element => element is { Type: "Item", IdIsStored: false } && Equals(element.Attributes["label"], "Has no id"));

        // Act.
        Assert.IsType<PlanResult.Planned>(body.Change(new ModelChange.Remove(anonymous.Id)));

        // Assert: a task with no owner names nothing, so it does not go with an item that has no id.
        Assert.Equal(["of-the-first", "of-the-other", "owned-by-nobody"], Tasks(body));
    }

    private static OpenBody Open(string by) =>
        OpenBody.Open(Encoding.UTF8.GetBytes(Body.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n"), by == "by id" ? _binding : _byAttribute);

    private static List<string> Tasks(OpenBody body) => [.. body.Model.Elements.Where(element => element.Type == "Task").Select(element => element.Id)];

    private static FblBinding Inline(string elements)
    {
        var json = $$"""{ "fbl": "0.5", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": {{elements}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == ProblemSeverity.Error);
        return document!.Bindings["t"];
    }
}
