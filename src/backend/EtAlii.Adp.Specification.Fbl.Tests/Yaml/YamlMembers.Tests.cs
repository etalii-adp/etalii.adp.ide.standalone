using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Yaml;

/// <summary>
/// FBL 0.14's new member of a mapping, named by its key (section 6.2), for the cases the conformance fixtures do not reach:
/// a container written as its key alone, one written in flow form, a member placed at the start, a key that is not
/// plain-safe, and the same member in json beside a skeleton.
/// </summary>
public class YamlMembersTests
{
    private static FblBinding Load(string family, string place, string? skeleton = null)
    {
        var written = skeleton is null ? "" : $$""", "skeleton": "{{skeleton}}" """;
        var json = $$"""
            { "fbl": "0.14", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "{{family}}" }, "reader": "declared",
              "elements": [{ "name": "lane", "type": "Lane", "at": "/lanes/{lane}", "id": { "from": { "capture": "lane" } },
                "attributes": { "key": { "capture": "lane", "readOnly": "A lane is renamed in the file." }, "title": { "key": "title" } },
                "insert": { "place": "{{place}}", "container": "/lanes", "keys": ["title"]{{written}} }, "remove": {} }] } } }
            """;
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return document!.Bindings["t"];
    }

    private static PlanResult Add(OpenBody body, string key) =>
        body.Change(new ModelChange.Add("Lane", null, new Dictionary<string, object?> { ["key"] = key, ["title"] = "T" }));

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);

    [Fact]
    public void AContainerWrittenAsItsKeyAlone_TakesTheMemberAsTheFirstOfABlockMapping()
    {
        // Arrange.
        var body = OpenBody.Open(Encoding.UTF8.GetBytes("board: x\nlanes:\n"), Load("yaml", "after-last"));

        // Act.
        var result = Add(body, "todo");

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("board: x\nlanes:\n  todo:\n    title: T\n", Text(body));
    }

    [Fact]
    public void AContainerWrittenInFlowForm_TakesNoMember()
    {
        // Arrange.
        var body = OpenBody.Open(Encoding.UTF8.GetBytes("lanes: {}\n"), Load("yaml", "after-last"));

        // Act.
        var result = Add(body, "todo");

        // Assert.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal("lanes: {}\n", Text(body));
    }

    [Fact]
    public void AMemberPlacedAtTheStart_GoesBeforeTheFirst_WithItsKeyQuotedWhenItIsNotPlainSafe()
    {
        // Arrange.
        var body = OpenBody.Open(Encoding.UTF8.GetBytes("lanes:\n  todo:\n    title: To do\n"), Load("yaml", "start"));

        // Act.
        var result = Add(body, "yes");

        // Assert: "yes" written plain would read as a boolean, not as the key the add gave.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("lanes:\n  \"yes\":\n    title: T\n  todo:\n    title: To do\n", Text(body));
        Assert.Contains(body.Model.Elements, element => element.Type == "Lane" && element.Id == "yes");
    }

    [Fact]
    public void AJsonMemberBesideASkeleton_IsRefused_RatherThanWrittenIncomplete()
    {
        // Arrange.
        var text = "{\n  \"lanes\": {\n    \"todo\": {\n      \"title\": \"To do\"\n    }\n  }\n}\n";
        var body = OpenBody.Open(Encoding.UTF8.GetBytes(text), Load("json", "after-last", "cards: []\\n"));

        // Act.
        var result = Add(body, "done");

        // Assert.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal(text, Text(body));
    }

    [Fact]
    public void AJsonMemberPlacedAtTheStart_TakesTheSeparatorAfterIt()
    {
        // Arrange.
        var body = OpenBody.Open(Encoding.UTF8.GetBytes("{\n  \"lanes\": {\n    \"todo\": {\n      \"title\": \"To do\"\n    }\n  }\n}\n"), Load("json", "start"));

        // Act.
        var result = Add(body, "first");

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("{\n  \"lanes\": {\n    \"first\": {\n      \"title\": \"T\"\n    },\n    \"todo\": {\n      \"title\": \"To do\"\n    }\n  }\n}\n", Text(body));
    }
}
