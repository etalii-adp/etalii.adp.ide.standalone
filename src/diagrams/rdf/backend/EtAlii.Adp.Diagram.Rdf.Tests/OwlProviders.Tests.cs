using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The ontology cases of the family providers: the subclass gesture with its duplicate refusal,
/// the four palette adds writing declaration triples, the punned individual role editing like a
/// resource, the expression grid's uncapped Manchester with the boundary sentence, and the
/// derived property edge's axiom rows (owl-diagram Requirements 6 and 7).
/// </summary>
public class OwlProvidersTests : IDisposable
{
    private const string Ns = "http://example.org/o#";

    private const string Corpus =
        "@prefix : <http://example.org/o#> .\r\n"
        + "@prefix owl: <http://www.w3.org/2002/07/owl#> .\r\n"
        + "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\r\n"
        + "\r\n"
        + "<http://example.org/o> a owl:Ontology .\r\n"
        + "\r\n"
        + ":Pizza a owl:Class , owl:NamedIndividual .\r\n"
        + "\r\n"
        + ":Topping a owl:Class .\r\n"
        + "\r\n"
        + ":Vegetarian a owl:Class ;\r\n"
        + "    rdfs:subClassOf :Pizza ;\r\n"
        + "    rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :hasTopping ; owl:someValuesFrom :Topping ] .\r\n"
        + "\r\n"
        + ":hasTopping a owl:ObjectProperty , owl:FunctionalProperty ;\r\n"
        + "    rdfs:domain :Pizza ;\r\n"
        + "    rdfs:range :Topping .\r\n";

    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly RdfContextActionProvider _actions;
    private readonly RdfContextPropertyProvider _properties;
    private readonly IHistoryStack _history;

    public OwlProvidersTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddRdf()
            .BuildServiceProvider();
        _actions = new RdfContextActionProvider(
            _provider.GetRequiredService<IHistoryStackStore>(),
            _provider.GetRequiredService<IRdfDocumentStore>());
        _properties = new RdfContextPropertyProvider(
            _provider.GetRequiredService<IHistoryStackStore>(),
            _provider.GetRequiredService<IRdfDocumentStore>());
        _history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string WriteBody(string content, string name = "ontology.ttl")
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private ContextTarget Target(string bodyPath, string elementId) => new(
        ContextScope.DiagramElement, bodyPath, IsContainer: false, SourceId: default, _root, default, elementId);

    private async Task<IReadOnlyList<string>> ActionIds(string bodyPath, string elementId)
    {
        var groups = await _actions.DiscoverAsync(Target(bodyPath, elementId), TestContext.Current.CancellationToken);
        return groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();
    }

    [Fact]
    public async Task TheSubclassGesture_SplicesOneTriple_OneUndoAway()
    {
        // Arrange: a gesture from one class to another.
        var body = WriteBody(Corpus);
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var gesture = RdfRelationGesture.IdFor($"res:{Ns}Topping", $"res:{Ns}Pizza");

        // Act & assert: between two classes the gesture offers the subclass action first.
        var offered = await ActionIds(body, gesture);
        Assert.Contains(RdfContextActionProvider.SubclassActionId, offered);
        Assert.Contains(RdfContextActionProvider.ConnectActionId, offered);

        // Executing splices exactly one rdfs:subClassOf statement, no dialog (Requirement 6.1).
        var execution = await _actions.ExecuteAsync(
            Target(body, gesture), RdfContextActionProvider.SubclassActionId, TestContext.Current.CancellationToken);
        Assert.IsType<ContextExecutionCompleted>(execution);
        var model = RdfParser.Parse(RdfDocument.Parse(await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)));
        Assert.Contains(model.Triples, t =>
            t.Subject is IriTerm { Iri: $"{Ns}Topping" }
            && t.Predicate.Iri == OwlVocabulary.SubClassOf
            && t.Object is IriTerm { Iri: $"{Ns}Pizza" });

        // And the gesture is one undo away, byte for byte.
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADuplicateSubclassGesture_IsRefusedBeforeAnySplice()
    {
        // Arrange: Vegetarian already asserts Pizza.
        var body = WriteBody(Corpus);
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var gesture = RdfRelationGesture.IdFor($"res:{Ns}Vegetarian", $"res:{Ns}Pizza");

        // Act.
        var execution = await _actions.ExecuteAsync(
            Target(body, gesture), RdfContextActionProvider.SubclassActionId, TestContext.Current.CancellationToken);

        // Assert: refused with the sentence, the file untouched (Requirement 6.1).
        var failed = Assert.IsType<ContextExecutionFailed>(execution);
        Assert.Equal(OwlSelection.DuplicateSubclassRefusal, failed.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ThePaletteAdds_AppearOnMarkedDocuments_AndWriteDeclarations()
    {
        // Arrange.
        var body = WriteBody(Corpus);
        var placement = RdfNewPlacement.IdFor(10, 20);

        // Act & assert: the ontology document offers the OWL adds; a plain graph does not.
        var marked = await ActionIds(body, placement);
        Assert.Contains(RdfContextActionProvider.AddClassActionId, marked);
        Assert.Contains(RdfContextActionProvider.AddIndividualActionId, marked);
        var plain = WriteBody("@prefix ex: <http://example.org/> .\r\n\r\nex:a ex:knows ex:b .\r\n", "plain.ttl");
        var unmarked = await ActionIds(plain, RdfNewPlacement.IdFor(1, 2));
        Assert.DoesNotContain(RdfContextActionProvider.AddClassActionId, unmarked);

        // Committing an add writes exactly one declaration triple (Requirement 6.2).
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var commit = await _actions.CommitAsync(
            Target(body, placement), RdfContextActionProvider.AddClassActionId, ":Dessert", "", TestContext.Current.CancellationToken);
        Assert.Equal("", commit.Error);
        var model = RdfParser.Parse(RdfDocument.Parse(await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)));
        Assert.Contains(model.Triples, t =>
            t.Subject is IriTerm { Iri: $"{Ns}Dessert" }
            && t.Predicate.Iri == RdfVocabulary.Type
            && t.Object is IriTerm { Iri: OwlVocabulary.Class });
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APunnedIndividualRole_EditsLikeAResource()
    {
        // Arrange: :Pizza is class and individual; the individual role selects as ind: (Requirement 2.4).
        var body = WriteBody(Corpus);

        // Act & assert: rename and remove are on offer for the ind: id.
        var offered = await ActionIds(body, $"ind:{Ns}Pizza");
        Assert.Contains(RdfContextActionProvider.RenameResourceActionId, offered);
        Assert.Contains(RdfContextActionProvider.RemoveResourceActionId, offered);

        // A label edit through the grid lands as one triple, one undo away (Requirement 6.4).
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var set = await _properties.SetAsync(
            Target(body, $"ind:{Ns}Pizza"), RdfContextPropertyProvider.LabelProperty, "The pizza", TestContext.Current.CancellationToken);
        Assert.True(set.IsSuccess);
        Assert.Contains("The pizza", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnExpressionSelection_ShowsTheUncappedForm_ReadOnlyWithTheBoundarySentence()
    {
        // Arrange.
        var body = WriteBody(Corpus);
        var expressionId = $"expr:{Ns}Vegetarian|{OwlVocabulary.SubClassOf}|1";

        // Act.
        var rows = await _properties.DescribeAsync(Target(body, expressionId), TestContext.Current.CancellationToken);
        var offered = await ActionIds(body, expressionId);

        // Assert: the full Manchester form, read-only for the boundary's reason, and no actions
        // at all (Requirements 3.2, 6.5, 7.2).
        var row = Assert.Single(rows);
        Assert.Equal("∃ :hasTopping.:Topping", row.Value);
        Assert.Equal(OwlSelection.ExpressionRefusal, row.ReadOnlyReason);
        Assert.Empty(offered);
    }

    [Fact]
    public async Task AClassSelection_CarriesItsAxiomRows()
    {
        // Arrange.
        var body = WriteBody(Corpus);

        // Act.
        var rows = await _properties.DescribeAsync(Target(body, $"res:{Ns}Vegetarian"), TestContext.Current.CancellationToken);

        // Assert: the named superclass and the expression axiom, the latter in Manchester form
        // with the boundary as its read-only reason (Requirement 7.2).
        Assert.Contains(rows, r => r.Label == "Subclass of" && r.Value == ":Pizza");
        Assert.Contains(rows, r => r.Label == "Subclass of" && r.Value == "∃ :hasTopping.:Topping"
            && r.ReadOnlyReason == OwlSelection.ExpressionRefusal);
    }

    [Fact]
    public async Task ADerivedPropertyEdge_ShowsThePropertysAxioms()
    {
        // Arrange: the domain-to-range edge is a derived element, not one triple (Requirement 7.3).
        var body = WriteBody(Corpus);
        var edgeId = $"edge:res:{Ns}Pizza|{Ns}hasTopping|res:{Ns}Topping";

        // Act.
        var rows = await _properties.DescribeAsync(Target(body, edgeId), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(rows, r => r.Label == "Domain" && r.Value == ":Pizza");
        Assert.Contains(rows, r => r.Label == "Range" && r.Value == ":Topping");
        Assert.Contains(rows, r => r.Label == "Characteristic" && r.Value == "functional");
    }

    [Fact]
    public void TheOwlToolbox_OffersTheFourEntries_AndNoRestriction()
    {
        // Arrange & act.
        var toolbox = _provider.GetServices<IDiagramToolboxProvider>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddRdfExtension.OwlOrigin);

        // Assert (Requirement 7.1).
        Assert.Equal(4, toolbox.Items.Count);
        Assert.DoesNotContain(toolbox.Items, item => item.Label.Contains("Restriction", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(toolbox.Items, item => item.DropActionId == RdfContextActionProvider.AddClassActionId);
        Assert.Contains(toolbox.Items, item => item.DropActionId == RdfContextActionProvider.AddIndividualActionId);
    }
}
