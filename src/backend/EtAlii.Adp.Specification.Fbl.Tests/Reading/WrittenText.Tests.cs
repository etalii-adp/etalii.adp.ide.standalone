using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Json;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;
using EtAlii.Adp.Specification.Fbl.Yaml;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Reading;

/// <summary>
/// What a slot reads from a yaml scalar (FBL 0.5 §5.2, §5.3; Requirement 1.1 of fbl-written-text):
/// a string attribute, an id and a reference read what the author wrote. And the written text the
/// yaml and json families give every scalar (FBL 0.5 §4.1.5; Requirement 2.1).
/// </summary>
public class WrittenTextTests
{
    private static readonly FblBinding _binding = Inline("""
        [{
          "name": "item", "type": "Item", "at": "/items/*",
          "id": { "from": { "key": "id" } },
          "attributes": { "label": { "key": "label" } }
        },
        {
          "name": "task", "type": "Task", "at": "/tasks/*",
          "id": { "from": { "key": "id" } },
          "attributes": { "owner": { "key": "owner", "reference": { "to": ["item"], "by": "id" } } }
        }]
        """);

    private static readonly FblBinding _relations = Inline("""
        [{ "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } } }],
        "relations": [{ "name": "link", "type": "Link", "at": "/links/*", "source": { "key": "from" }, "target": { "key": "to" } }]
        """);

    private static readonly FblBinding _named = Inline("""
        [{
          "name": "item", "type": "Item", "at": "/items/*",
          "id": { "from": { "key": "id" } },
          "attributes": { "code": { "key": "id" } },
          "remove": { "cascade": ["task"] }
        },
        {
          "name": "task", "type": "Task", "at": "/tasks/*",
          "id": { "from": { "key": "id" } },
          "attributes": { "owner": { "key": "owner", "reference": { "to": ["item"], "by": "id" } } },
          "remove": {}
        }]
        """);

    private static readonly FblOptions _strings = new() { FileName = "plan.t", AttributeType = (_, attribute) => attribute == "label" ? "string" : null };

    /// <summary>Each case of Requirement 1.1: what is read, and what FBL 0.5 says it reads.</summary>
    private static readonly string[] _names = ["label yes", "label 1.0", "label null", "label ~", "id 007", "reference 007"];

    public static TheoryData<string> Cases => new(_names);

    [Theory]
    [MemberData(nameof(Cases))]
    public void AScalarReadsAsWritten(string name)
    {
        // Arrange.
        (string body, object? expected, Func<FblModel, object?> observe, OpenFinding? open) = Case(name);

        // Act.
        var observed = observe(Read(body));

        // Assert: an open finding still reads as it does today, until the task that repairs it.
        if (open is not null)
        {
            Assert.Equal(open.Today, observed);
            Assert.NotEqual(expected, observed);
        }
        else
        {
            Assert.Equal(expected, observed);
        }
    }

    /// <summary>
    /// Task 5, the last that changes a reading, makes this assert that no mark is left; until then
    /// each mark names the task that repairs it.
    /// </summary>
    [Fact]
    public void EveryOpenFindingNamesTheTaskThatRepairsIt()
    {
        // Act.
        var marks = _names.Select(n => Case(n).Open).OfType<OpenFinding>().ToList();

        // Assert.
        Assert.All(marks, mark => Assert.Equal(4, mark.RepairedBy));
    }

    /// <summary>A case of Requirement 1.1 the library does not yet read as FBL 0.5 says: what it reads today, and which task of fbl-written-text repairs it.</summary>
    private sealed record OpenFinding(object? Today, int RepairedBy);

    private static (string Body, object? Expected, Func<FblModel, object?> Observe, OpenFinding? Open) Case(string name) => name switch
    {
        // Green against the untouched library: it reads yaml by the 1.2 core schema, where yes is a string.
        "label yes" => ("items:\n  - id: a\n    label: yes\n", "yes", m => m.Find("a")!.Attributes["label"], null),
        "label 1.0" => ("items:\n  - id: a\n    label: 1.0\n", "1.0", m => m.Find("a")!.Attributes["label"], new OpenFinding(1.0, 4)),
        "label null" => ("items:\n  - id: a\n    label: null\n", "null", m => m.Find("a")!.Attributes["label"], new OpenFinding(null, 4)),
        "label ~" => ("items:\n  - id: a\n    label: ~\n", "~", m => m.Find("a")!.Attributes["label"], new OpenFinding(null, 4)),
        "id 007" => ("items:\n  - id: 007\n    label: Seven\n", "007", m => m.Elements.Single().Id, null),
        // The element named is written quoted, so its id is "007" whatever is read; the reference is written plain.
        "reference 007" => ("items:\n  - id: \"007\"\ntasks:\n  - id: b\n    owner: 007\n", "", m => string.Join(" ", m.Findings.Select(f => f.Message)), null),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [Theory]
    [InlineData("~", "~")]
    [InlineData("null", "null")]
    [InlineData("'007'", "007")]
    public void AnIdThatYamlTypesIsItsWrittenText(string written, string id)
    {
        // Act.
        var model = Read($"items:\n  - id: {written}\n");

        // Assert.
        var element = Assert.Single(model.Elements);
        Assert.Equal((id, true), (element.Id, element.IdIsStored));
        Assert.Empty(model.Findings);
    }

    [Fact]
    public void AnIdWrittenWithNoValueIsMissing()
    {
        // Act.
        var model = Read("items:\n  - id:\n");

        // Assert: addressed by its place, and reported.
        Assert.False(Assert.Single(model.Elements).IdIsStored);
        Assert.Equal(FindingCodes.MissingId, Assert.Single(model.Findings).Code);
    }

    [Fact]
    public void ARelationsEndIsItsWrittenText()
    {
        // Act.
        var model = OpenBody.Open("items:\n  - id: \"007\"\n  - id: b\nlinks:\n  - from: 007\n    to: b\n"u8.ToArray(), _relations, _strings).Model;

        // Assert.
        var link = Assert.Single(model.Elements, e => e.IsRelation);
        Assert.Equal(("007", "b"), (link.Source, link.Target));
        Assert.Empty(model.Findings);
    }

    [Fact]
    public void ARenameRewritesAReferenceWrittenAsTheOldKey()
    {
        // Arrange: the id is written quoted, the reference plain; both are the text 007.
        var body = OpenBody.Open("items:\n  - id: \"007\"\ntasks:\n  - id: b\n    owner: 007\n"u8.ToArray(), _named, _strings);

        // Act.
        var result = body.Change(new ModelChange.Set("007", new Dictionary<string, object?> { ["code"] = "008" }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: \"008\"\ntasks:\n  - id: b\n    owner: \"008\"\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void ARemovalCascadesToAnEntryReferencingItsWrittenText()
    {
        // Arrange.
        var body = OpenBody.Open("items:\n  - id: \"007\"\ntasks:\n  - id: b\n    owner: 007\n"u8.ToArray(), _named, _strings);

        // Act.
        var result = body.Change(new ModelChange.Remove("007"));

        // Assert: the task naming it goes with it.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Empty(body.Model.Elements);
    }

    [Theory]
    [InlineData("k: Plan  the launch  # a comment\n", "Plan  the launch")]
    [InlineData("k: one\n  two\n", "one two")]
    [InlineData("k: 007\n", "007")]
    [InlineData("k: 1.0\n", "1.0")]
    [InlineData("k: ~\n", "~")]
    [InlineData("k: 'it''s'\n", "it's")]
    [InlineData("k: \"a\\tb\\u00e9\\\"\"\n", "a\tb\u00e9\"")]
    [InlineData("k: |\n  one\n  two\n", "one\ntwo\n")]
    [InlineData("k: >\n  one\n  two\n", "one two\n")]
    [InlineData("k:\n", "")]
    [InlineData("a: &x 007\nk: *x\n", "007")]
    public void AYamlScalarIsWrittenAsItsText(string body, string written)
    {
        // Arrange.
        var family = Yaml(body);

        // Act.
        var slot = family.ReadRaw(Root(family), "k");

        // Assert.
        Assert.Equal(written, slot.Written);
    }

    [Theory]
    [InlineData("""{ "k": "a\"b\u00e9" }""", "a\"b\u00e9")]
    [InlineData("""{ "k": 1.50 }""", "1.50")]
    [InlineData("""{ "k": -0e3 }""", "-0e3")]
    [InlineData("""{ "k": true }""", "true")]
    [InlineData("""{ "k": false }""", "false")]
    [InlineData("""{ "k": null }""", "null")]
    public void AJsonScalarIsWrittenAsItsText(string body, string written)
    {
        // Arrange.
        var family = Json(body);

        // Act.
        var slot = family.ReadRaw(Root(family), "k");

        // Assert.
        Assert.Equal(written, slot.Written);
    }

    [Fact]
    public void AValueReachedThroughAMergeKeyIsWrittenAsItsText()
    {
        // Arrange.
        var family = Yaml("base: &b\n  k: 007\nm:\n  <<: *b\n");
        var merged = Root(family).Value.Member("m")!;

        // Act.
        var slot = family.ReadRaw(merged, "k");

        // Assert: readable, never writable, and what was written.
        Assert.False(slot.Writable);
        Assert.Equal("007", slot.Written);
    }

    [Fact]
    public void TheMembersOfAFlowCollectionAreWrittenAsTheirText()
    {
        // Arrange.
        var family = Yaml("k: [007, yes, 'a, b', 1.0]\nm: {007: ~, n: \"x\"}\n");

        // Act.
        var sequence = Root(family).Value.Member("k")!.Value;
        var mapping = Root(family).Value.Member("m")!.Value;

        // Assert: the typed members are unchanged; the written ones are what the author wrote.
        Assert.Equal(new object?[] { 7L, "yes", "a, b", 1.0 }, (List<object?>)sequence.Flow!);
        Assert.Equal(new object?[] { "007", "yes", "a, b", "1.0" }, (List<object?>)sequence.FlowText!);
        Assert.Equal(new Dictionary<string, object?> { ["007"] = "~", ["n"] = "x" }, (IDictionary<string, object?>)mapping.FlowText!);
    }

    [Fact]
    public void AFlowCollectionIsNotAWrittenScalar()
    {
        // Arrange.
        var family = Yaml("k: [a, b]\n");

        // Act.
        var slot = family.ReadRaw(Root(family), "k");

        // Assert: a flow collection has the kind of a scalar, and its written text is not its raw characters.
        Assert.False(slot.WrittenScalar);
    }

    private static YamlFamily Yaml(string body)
    {
        var family = new YamlFamily(new BodyText(Encoding.UTF8.GetBytes(body)), _binding, _strings);
        family.Parse();
        Assert.Null(family.Unreadable);
        return family;
    }

    private static JsonFamily Json(string body)
    {
        var family = new JsonFamily(new BodyText(Encoding.UTF8.GetBytes(body)), _binding, _strings);
        family.Parse();
        Assert.Null(family.Unreadable);
        return family;
    }

    private static TreeEntry Root(TreeFamily family) => family.Root!;

    private static FblModel Read(string body) => OpenBody.Open(Encoding.UTF8.GetBytes(body), _binding, _strings).Model;

    private static FblBinding Inline(string elements)
    {
        var json = $$"""{ "fbl": "0.1", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": {{elements}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.Empty(problems);
        return document!.Bindings["t"];
    }
}
