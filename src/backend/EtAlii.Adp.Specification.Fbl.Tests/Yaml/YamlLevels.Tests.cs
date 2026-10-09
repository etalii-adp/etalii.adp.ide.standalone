using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Yaml;

/// <summary>
/// FBL 0.4 for the cases the conformance fixtures do not reach: a key written into a child mapping
/// that exists (section 5.2), a level that is there with nothing under it, a level emptied by a
/// value that is removed, and a missing level that a <c>*</c> names (section 6.2).
/// </summary>
public class YamlLevelsTests
{
    private static readonly FblBinding _binding = Load("""
        [{
          "name": "doc", "type": "Doc", "at": "/",
          "attributes": {
            "shown": { "key": "shown", "child": "view", "create": { "at": "end" } },
            "mode": { "key": "mode", "child": "view/options", "empty": "remove", "create": { "at": "end" } }
          }
        },
        {
          "name": "pin", "type": "Pin", "at": "/view/pins/*", "id": { "from": { "key": "id" } },
          "insert": { "place": "after-last", "container": "/view/pins", "create": { "at": "end" }, "keys": ["id"] },
          "remove": { "container": "remove-empty-levels" }
        },
        {
          "name": "mark", "type": "Mark", "at": "/groups/*/marks/*", "id": { "from": { "key": "id" } },
          "insert": { "place": "after-last", "container": "/groups/*/marks", "create": { "at": "end" }, "keys": ["id"] }
        }]
        """);

    private static FblBinding Load(string elements)
    {
        var json = $$"""{ "fbl": "0.4", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": {{elements}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == ProblemSeverity.Error);
        return document!.Bindings["t"];
    }

    private static OpenBody Open(string body) => OpenBody.Open(Encoding.UTF8.GetBytes(body), _binding);

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);

    private static string Doc(OpenBody body) => body.Model.Elements.Single(element => element.Type == "Doc").Id;

    private static PlanResult Set(OpenBody body, string attribute, object? value) =>
        body.Change(new ModelChange.Set(Doc(body), new Dictionary<string, object?> { [attribute] = value }));

    [Fact]
    public void AKeyTheRuleBindsNothingBefore_BecomesTheFirstKeyOfAMappingThatExists()
    {
        // Arrange.
        var body = Open("title: x\nview:\n  pins:\n    - id: a\n");

        // Act.
        var result = Set(body, "shown", true);

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("title: x\nview:\n  shown: true\n  pins:\n    - id: a\n", Text(body));
    }

    [Fact]
    public void ALevelThatIsThereWithNothingUnderIt_TakesTheKeyOneStepDeeper()
    {
        // Arrange.
        var body = Open("title: x\nview:\n");

        // Act.
        var result = Set(body, "shown", true);

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("title: x\nview:\n  shown: true\n", Text(body));
    }

    [Fact]
    public void EveryMissingLevelOfAChild_IsCreatedOutermostFirst_AndGoesAgainWithItsLastKey()
    {
        // Arrange.
        var body = Open("title: x\n");

        // Act.
        var set = Set(body, "mode", "wide");
        var written = Text(body);
        var cleared = Set(body, "mode", "");

        // Assert: both levels created with the value, and both gone with it.
        Assert.IsType<PlanResult.Planned>(set);
        Assert.Equal("title: x\nview:\n  options:\n    mode: wide\n", written);
        Assert.IsType<PlanResult.Planned>(cleared);
        Assert.Equal("title: x\n", Text(body));
    }

    [Fact]
    public void ALevelThatStillHoldsSomething_StaysWhenTheLevelUnderItGoes()
    {
        // Arrange.
        var body = Open("title: x\nview:\n  shown: true\n  pins:\n    - id: a\n");

        // Act.
        var result = body.Change(new ModelChange.Remove("a"));

        // Assert: pins goes with its last entry; view keeps its other key.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("title: x\nview:\n  shown: true\n", Text(body));
    }

    [Fact]
    public void AMissingLevelAStarNames_GivesNoKeyToWrite_SoTheInsertIsRefused()
    {
        // Arrange.
        var body = Open("title: x\n");

        // Act.
        var result = body.Change(new ModelChange.Add("Mark", "m", new Dictionary<string, object?>()));

        // Assert.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal("title: x\n", Text(body));
    }
}
