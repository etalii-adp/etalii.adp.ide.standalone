using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Planning;

/// <summary>
/// What FBL 0.10 gives a binding's writes beside the conformance fixtures (sections 5.2, 6.2 and 6.4): a nested mapping
/// written on an add, in yaml two levels deep and in json, a secondary removed with an emptied attribute, and the
/// sentences a binding gives its refusals, with the host's own where it gives none.
/// </summary>
public class SharedReadingWritesTests
{
    private static FblBinding Load(string family, string elements)
    {
        var json = $$"""
            { "fbl": "0.10", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "{{family}}" }, "reader": "declared",
              "elements": {{elements}} } } }
            """;
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return document!.Bindings["t"];
    }

    private const string Libraries = """
        [{ "name": "doc", "type": "Doc", "at": "/",
           "attributes": {
             "name": { "key": "name", "empty": "refuse", "refusals": { "empty": "A pipeline needs a name." } },
             "schema": { "key": "schema", "empty": "remove", "secondary": { "key": "target" } },
             "channel": { "key": "channel", "absent": { "yaml": "refuse", "json": "refuse" }, "refusals": { "absent": "The settings file has no \"channel\" to rewrite." } },
             "catalog": { "key": "catalog", "absent": { "yaml": "refuse", "json": "refuse" } } } },
         { "name": "library", "type": "Library", "at": "/libraries/*",
           "attributes": { "path": { "key": "path", "child": "notebook/source" }, "kind": { "key": "kind" } },
           "insert": { "place": "after-last", "container": "/libraries", "when": "attributes.kind != 'glob'", "refusals": { "when": "A glob library is added in the file." } } }]
        """;

    private static OpenBody Open(string family, string body) => OpenBody.Open(Encoding.UTF8.GetBytes(body), Load(family, Libraries));

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);

    private static PlanResult Set(OpenBody body, string attribute, object? value) =>
        body.Change(new ModelChange.Set(body.Model.Elements.Single(element => element.Type == "Doc").Id, new Dictionary<string, object?> { [attribute] = value }));

    [Fact]
    public void AnAdd_WritesAnAttributeBoundWithChild_UnderEveryLevelOfIt_InYaml()
    {
        // Arrange.
        var body = Open("yaml", "name: p\nlibraries:\n  - kind: file\n");

        // Act.
        var added = body.Change(new ModelChange.Add("Library", null, new Dictionary<string, object?> { ["kind"] = "notebook", ["path"] = "a/b" }));

        // Assert: the other keys first, then the levels, each one step deeper.
        Assert.IsType<PlanResult.Planned>(added);
        Assert.Equal("name: p\nlibraries:\n  - kind: file\n  - kind: notebook\n    notebook:\n      source:\n        path: a/b\n", Text(body));
        Assert.Equal("a/b", body.Model.Elements.Last().Attributes["path"]);
    }

    [Fact]
    public void AnAdd_WritesAnAttributeBoundWithChild_AsAMemberWhoseValueIsAnObject_InJson()
    {
        // Arrange.
        var body = Open("json", "{ \"name\": \"p\", \"libraries\": [ { \"kind\": \"file\" } ] }");

        // Act.
        body.Change(new ModelChange.Add("Library", null, new Dictionary<string, object?> { ["kind"] = "notebook", ["path"] = "a/b" }));

        // Assert.
        Assert.Equal("{ \"name\": \"p\", \"libraries\": [ { \"kind\": \"file\" }, { \"kind\": \"notebook\", \"notebook\": { \"source\": { \"path\": \"a/b\" } } } ] }", Text(body));
        Assert.Equal("a/b", body.Model.Elements.Last().Attributes["path"]);
    }

    [Fact]
    public void AnAttributeReadFromItsSecondary_WhenEmptied_RemovesTheSecondary()
    {
        // Arrange.
        var body = Open("yaml", "name: p\ntarget: silver\n");
        Assert.Equal("silver", body.Model.Elements.Single(element => element.Type == "Doc").Attributes["schema"]);

        // Act.
        var emptied = Set(body, "schema", "");

        // Assert: the value does not come back from the secondary.
        Assert.IsType<PlanResult.Planned>(emptied);
        Assert.Equal("name: p\n", Text(body));
    }

    [Theory]
    [InlineData("name", "", "A pipeline needs a name.")]
    [InlineData("channel", "CURRENT", "The settings file has no \"channel\" to rewrite.")]
    public void ARefusal_HasTheSentenceTheBindingGives(string attribute, string value, string sentence)
    {
        // Arrange.
        var body = Open("yaml", "name: p\n");

        // Act.
        var refused = Assert.IsType<PlanResult.Refused>(Set(body, attribute, value));

        // Assert.
        Assert.Equal(sentence, refused.Reason);
        Assert.Equal("name: p\n", Text(body));
    }

    [Fact]
    public void AnAddWhoseWhenDoesNotHold_IsRefusedWithTheInsertsSentence()
    {
        // Arrange.
        var body = Open("yaml", "name: p\nlibraries:\n  - kind: file\n");

        // Act.
        var refused = Assert.IsType<PlanResult.Refused>(body.Change(new ModelChange.Add("Library", null, new Dictionary<string, object?> { ["kind"] = "glob" })));

        // Assert.
        Assert.Equal("A glob library is added in the file.", refused.Reason);
    }

    [Fact]
    public void OfTheRulesOfOneType_TheFirstWhoseWhenHolds_PlansTheAdd()
    {
        // Arrange: a pipeline of the Databricks binding, whose notebook, file and glob libraries are rules of one type.
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, "databricks.fbl"), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        var body = OpenBody.Open("resources:\n  pipelines:\n    p:\n      name: P\n      libraries:\n        - notebook:\n            path: a\n"u8.ToArray(), document!.Bindings["configuration"], new FblOptions { FileName = "p.yml", Origin = "databricks/pipeline" });

        // Act: a file library, which the notebook rule's when refuses and the file rule's admits.
        var added = body.Change(new ModelChange.Add("Library", null, new Dictionary<string, object?> { ["libraryKind"] = "file", ["path"] = "b.py" }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(added);
        Assert.Equal("resources:\n  pipelines:\n    p:\n      name: P\n      libraries:\n        - notebook:\n            path: a\n        - file:\n            path: b.py\n", Text(body));
    }

    [Fact]
    public void ARefusalTheBindingGivesNoSentenceFor_IsWordedByTheHost()
    {
        // Arrange.
        var body = Open("yaml", "name: p\n");

        // Act.
        var refused = Assert.IsType<PlanResult.Refused>(Set(body, "catalog", "main"));

        // Assert.
        Assert.Equal("The t file has no \"catalog\" to rewrite.", refused.Reason);
    }
}
