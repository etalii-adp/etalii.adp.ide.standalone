using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The <c>natural</c> strategy as a binding's reader asks for it (DISL §11.5.1, <see cref="DislNaturalIds"/>):
/// an entry whose rule stores no id is read with the string forms of its type's <c>key</c> attributes,
/// joined by <c>|</c>, under the binding's names for them; the prefix is <see cref="DislModelBuilder"/>'s.
/// </summary>
public class NaturalIdDerivationTests
{
    private static DislSpecification Specification(string strategy) => Specifications.Loaded(Specifications.With($$"""
        "metamodel": {
          "types": { "Pump": { "attributes": { "tag": { "type": "string", "key": true }, "label": { "type": "string" } } },
                     "Valve": { "attributes": { "site": { "type": "string", "key": true }, "number": { "type": "int", "key": true } } },
                     "Note": { "attributes": { "text": { "type": "string" } } } },
          "relations": { "Pipe": { "source": ["Pump"], "target": ["Pump"],
                                   "attributes": { "fromTag": { "type": "string", "key": true }, "toTag": { "type": "string", "key": true } } } } },
        "persistence": {
          "ids": { "strategy": "{{strategy}}", "prefix": { "Pump": "pump:", "Pipe": "pipe:" } },
          "typeMap": { "Line": { "as": "Pipe", "attributes": { "from": "source", "to": "target", "a": "fromTag", "b": "toTag" } } } }
        """));

    private static readonly Func<IdRequest, string?> Natural = DislNaturalIds.Of(Specification("natural"));

    private static IdRequest Request(string type, params (string Name, object? Value)[] attributes) =>
        new(type.ToLowerInvariant(), type, attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value, StringComparer.Ordinal), null, null, 1);

    [Fact]
    public void ANodeIsReadWithItsKeyAttribute_WithoutThePrefix()
    {
        // Act and assert.
        Assert.Equal("p1", Natural(Request("Pump", ("tag", "p1"), ("label", "Pump one"))));
    }

    [Fact]
    public void SeveralKeyAttributes_AreJoinedByABar_InDeclarationOrder()
    {
        // Act and assert: a number is written in its invariant form.
        Assert.Equal("north|7", Natural(Request("Valve", ("number", 7L), ("site", "north"))));
    }

    [Fact]
    public void ABindingTypeIsMappedByTheTypeMap_AndItsKeysFoundUnderTheBindingsNames()
    {
        // Act and assert.
        Assert.Equal("p1|p2", Natural(Request("Line", ("from", "p1"), ("to", "p2"), ("a", "p1"), ("b", "p2"))));
    }

    [Fact]
    public void NoIdIsDerived_WithoutAKeyValue_WithoutAKeyAttribute_ForAnUnknownType_OrUnderAnotherStrategy()
    {
        // Act and assert.
        Assert.Null(Natural(Request("Pump", ("label", "No tag"))));
        Assert.Null(Natural(Request("Pump", ("tag", ""))));
        Assert.Null(Natural(Request("Note", ("text", "Hello"))));
        Assert.Null(Natural(Request("Gauge", ("tag", "g1"))));
        Assert.Null(DislNaturalIds.Of(Specification("uuid-v4"))(Request("Pump", ("tag", "p1"))));
    }

    [Fact]
    public void ABodyReadWithTheDerivation_GivesEachElementItsPrefixFollowedByItsKey_AndReferencesResolve()
    {
        // Arrange: a lines binding whose rules store no id, as a binding that leaves ids to DISL is.
        var specification = Specification("natural");
        var problems = FblDocumentLoader.Load("""
            {
              "fbl": "0.2",
              "bindings": {
                "plant": {
                  "title": "Plant",
                  "claims": { "extensions": [".plant"] },
                  "body": { "kind": "file", "family": "lines" },
                  "reader": "declared",
                  "elements": [
                    { "name": "pump", "type": "Pump", "line": "^pump (?<tag>\\S+)$",
                      "attributes": { "tag": { "group": "tag" } } },
                    { "name": "line", "type": "Line", "line": "^line (?<from>\\S+) (?<to>\\S+)$",
                      "attributes": { "from": { "group": "from", "word": "^.+$", "reference": { "to": ["pump"], "by": "tag" } },
                                      "to": { "group": "to", "word": "^.+$", "reference": { "to": ["pump"], "by": "tag" } },
                                      "a": { "value": "groups.from" }, "b": { "value": "groups.to" } } }
                  ]
                }
              }
            }
            """u8.ToArray(), out var document);
        Assert.NotNull(document);
        Assert.DoesNotContain(problems, problem => problem.Severity == ProblemSeverity.Error);
        var bytes = "pump p1\npump p2\nline p1 p2\n"u8.ToArray();

        // Act.
        var reading = OpenBody.Open(bytes, document.Bindings["plant"], new FblOptions { DeriveId = DislNaturalIds.Of(specification) }).Model;
        var diagram = DislModelBuilder.From(reading, specification).Diagram;

        // Assert.
        Assert.Equal(["pump:p1", "pump:p2", "pipe:p1|p2"], diagram.Elements.Select(element => element.Id));
        var pipe = Assert.Single(diagram.Relations);
        Assert.Equal(("pump:p1", "pump:p2"), (pipe.Source?.Id, pipe.Target?.Id));
    }
}
