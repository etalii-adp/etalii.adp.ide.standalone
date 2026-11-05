using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Bullets;

/// <summary>
/// The <c>bullets</c> family (FBL 0.7 §4.8, §6.3) on the cases the conformance fixtures do not hold: the kinds of
/// line that are not items, columns counted with tabs, sections, and the line ending and indentation an edit writes.
/// </summary>
public class BulletsFamilyTests
{
    private static readonly FblBinding _topics = Binding();

    private static FblBinding Binding()
    {
        const string json = """
            { "fbl": "0.7", "bindings": { "topics": {
              "claims": { "registrationOnly": true }, "body": { "kind": "file", "family": "bullets" }, "reader": "declared",
              "sections": [ { "name": "topics", "heading": "^Topics$" } ],
              "elements": [ {
                "name": "topic", "type": "Topic", "within": [ "topics" ], "line": "^\\[(?<id>[a-z0-9]+)\\] (?<title>.+)$",
                "id": { "from": { "group": "id" } }, "parent": { "rules": [ "topic" ], "slot": "children" },
                "attributes": { "title": { "group": "title" }, "notes": { "continuation": true } },
                "insert": { "place": "after-last", "container": "topics", "emit": "\\[{id}\\] {title}" }, "remove": {}
              } ]
            } } }
            """;
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return document!.Bindings["topics"];
    }

    private static OpenBody Open(string text) => OpenBody.Open(Encoding.UTF8.GetBytes(text), _topics, new FblOptions { FileName = "plan.md" });

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);

    [Fact]
    public void ItemsNestByTheirMarkersColumn_ATabCountingFourColumns_WithEveryMarker()
    {
        // Act: b's tab puts its marker at column 4, so c's at column 2 closes b and stands in a.
        var model = Open("## Topics\n\n- [a] A\n\t* [b] B\n  + [c] C\n").Model;

        // Assert.
        Assert.Equal(["a", "b", "c"], model.Elements.Select(element => element.Id));
        Assert.Null(model.Find("a")!.ParentId);
        Assert.Equal("a", model.Find("b")!.ParentId);
        Assert.Equal("a", model.Find("c")!.ParentId);
    }

    [Fact]
    public void AnItemLineInsideAFence_IsNotAnItem()
    {
        // Act.
        var model = Open("## Topics\n\n```\n- [x] X\n```\n- [a] A\n").Model;

        // Assert.
        Assert.Equal(["a"], model.Elements.Select(element => element.Id));
    }

    [Fact]
    public void ALineUnderlinedWithEquals_IsNoHeading_SoTheSectionIsMissing()
    {
        // Act.
        var model = Open("Topics\n======\n\n- [a] A\n").Model;

        // Assert.
        Assert.Empty(model.Elements);
    }

    [Fact]
    public void ASectionRunsToTheNextHeadingOfItsLevelOrHigher_PastDeeperOnes()
    {
        // Act.
        var model = Open("## Topics\n- [a] A\n### Detail\n- [b] B\n## Other\n- [c] C\n").Model;

        // Assert.
        Assert.Equal(["a", "b"], model.Elements.Select(element => element.Id));
    }

    [Fact]
    public void AnItemNestedInAnItemNoRuleReads_IsNotLookedInto()
    {
        // Act.
        var model = Open("## Topics\n- not a topic\n  - [b] B\n- [c] C\n").Model;

        // Assert.
        Assert.Equal(["c"], model.Elements.Select(element => element.Id));
    }

    [Fact]
    public void TheContinuation_LosesTheColumnsBeforeTheContentColumn_ATabItCutsIntoLeavingItsRest()
    {
        // Act: the content column is 2; the tab spans columns 0 to 4, so two of its columns remain as spaces.
        var model = Open("## Topics\n- [a] A\n\t  indented\n  plain\n").Model;

        // Assert.
        Assert.Equal("    indented\nplain", model.Find("a")!.Attributes["notes"]);
    }

    [Fact]
    public void ANewItem_TakesTheLineEndingOfTheLineItLandsOn()
    {
        // Arrange.
        var body = Open("## Topics\r\n\r\n- [a] A\r\n");

        // Act.
        var result = body.Change(new ModelChange.Add("Topic", "b", new Dictionary<string, object?> { ["title"] = "B" }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("## Topics\r\n\r\n- [a] A\r\n- [b] B\r\n", Text(body));
    }

    [Fact]
    public void AMoveIntoAnotherItem_ShiftsEveryLineThatIsNotBlankByTheDifferenceInDepth()
    {
        // Arrange.
        var body = Open("## Topics\n- [a] A\n- [b] B\n  note\n\n  more\n");

        // Act: b becomes a's first child, its marker at a's content column 2.
        var result = body.Change(new ModelChange.Move("b", "a", 0));

        // Assert: the blank line stays blank.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("## Topics\n- [a] A\n  - [b] B\n    note\n\n    more\n", Text(body));
        Assert.Equal("a", body.Model.Find("b")!.ParentId);
    }

    [Fact]
    public void AMoveIntoItsOwnDescendant_IsRefused_AndWritesNothing()
    {
        // Arrange.
        var input = "## Topics\n- [a] A\n  - [b] B\n";
        var body = Open(input);

        // Act.
        var result = body.Change(new ModelChange.Move("a", "b", 0));

        // Assert.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal(input, Text(body));
    }
}
