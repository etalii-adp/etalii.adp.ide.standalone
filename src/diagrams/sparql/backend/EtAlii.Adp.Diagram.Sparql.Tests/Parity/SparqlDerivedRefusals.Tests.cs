using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>
/// The refusals derived from the bundled definition are the hand-written session's and providers'
/// (<see cref="HandWrittenSparql"/>): the refusal of a move of every element of every document of the corpus, drawn or
/// cut, of the header band, the banner and an unknown id; the re-parenting, a property write and an action; and the
/// empty toolbox and menus (sparql-disl-fbl, Requirements 5.2 and 5.4).
/// </summary>
public class SparqlDerivedRefusalsTests
{
    public static TheoryData<string> Documents() => [.. SparqlCorpus.Documents()];

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryMoveRefusal_IsTheHandWrittenSessions(string document)
    {
        // Arrange.
        var text = File.ReadAllText(SparqlCorpus.Full(document));
        var outcome = SparqlParser.Outcome(text);
        var derived = new SparqlDocument(SparqlBody.Parse(text));
        var ids = SparqlDerived.Targets(HandWrittenSparql.Project(outcome.Model))
            .Concat(derived.Disl.Diagram.Elements.Select(element => element.Id).Where(SparqlDerived.IsWireId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Act and assert.
        foreach (var id in ids)
        {
            var actual = SparqlChrome.MoveRefusal(id) ?? SparqlDefinition.MoveRefusal(derived, id) ?? "";
            Assert.True(HandWrittenSparql.MoveRefusal(id) == actual, $"{document} {id}:\nexpected {HandWrittenSparql.MoveRefusal(id)}\nactual   {actual}");
        }
    }

    [Fact]
    public void TheReparentingRefusal_IsTheHandWrittenSessions() =>
        Assert.Equal(HandWrittenSparql.ParentRefusal, SparqlDefinition.ReparentRefusal);

    [Fact]
    public void TheRefusalOfAWrite_IsTheHandWrittenProviders() =>
        Assert.Equal(HandWrittenSparql.ReadOnlyReason, SparqlDefinition.Refusal);

    [Fact]
    public void TheRefusalOfAnAction_IsTheHandWrittenProviders() =>
        Assert.Equal(HandWrittenSparql.NoActionsReason, SparqlContextActionProvider.NoActionsReason);

    [Fact]
    public void TheDerivedToolbox_IsEmpty() => Assert.Empty(SparqlDefinition.Toolbox);

    [Theory]
    [MemberData(nameof(Documents))]
    public void NoElement_IsOfferedAnAction(string document)
    {
        // Arrange.
        var text = File.ReadAllText(SparqlCorpus.Full(document));
        var diagram = new SparqlDocument(SparqlBody.Parse(text)).Disl.Diagram;

        // Act.
        var offered = diagram.Elements
            .Select(element => (element.Id, Groups: ContextMenuDerivation.Derive(SparqlDefinition.Specification, DislMenuTarget.Element(element), new DislEnv(ReadOnly: true), SparqlDefinition.Ids)))
            .Append((Id: "canvas", Groups: ContextMenuDerivation.Derive(SparqlDefinition.Specification, DislMenuTarget.Canvas(diagram), new DislEnv(ReadOnly: true), SparqlDefinition.Ids)))
            .Where(target => target.Groups.Count > 0)
            .Select(target => target.Id);

        // Assert.
        Assert.Empty(offered);
    }
}
