using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The shapes reading's context surface (shacl-diagram Requirement 6), offered through the
/// family's one provider registration: scoped to targets whose origin is this reading's, with
/// the chip address making a target removable without ever being a selectable element, and the
/// gate refusing a blank-rooted selection before anything is executed.
/// </summary>
public class ShaclActionsTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly string _body;

    private const string Shapes = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.org/> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

        ex:PersonShape a sh:NodeShape ;
            sh:targetClass ex:Person ;
            sh:property [ sh:path ex:name ; sh:datatype xsd:string ] .
        """;

    public ShaclActionsTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _body = IoPath.Combine(_root, "shapes.ttl");
        File.WriteAllText(_body, Shapes);

        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddRdf()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    private RdfDocumentEntry Entry() => _provider.GetRequiredService<IRdfDocumentStore>().GetOrLoad(_body);

    private ContextTarget Target(string elementId, DiagramOrigin? origin) =>
        new(ContextScope.DiagramElement, _body, false, ShortGuid.NewShortGuid(), _root, default, elementId) { Origin = origin };

    private const string CardId = "res:http://example.org/PersonShape";

    [Fact]
    public void TheCases_AnswerForTheirOwnOrigin()
    {
        var groups = ShaclActions.Discover(Entry(), Target(CardId, ServiceCollectionAddShaclExtension.ShaclOrigin));

        var actions = Assert.Single(groups).Actions;
        Assert.Contains(actions, action => action.Id == ShaclActions.AddTargetClassActionId);
        Assert.Contains(actions, action => action.Id == ShaclActions.AddPropertyRowActionId);
        Assert.Contains(actions, action => action.Id == ShaclActions.DeactivateActionId);
        Assert.Contains(actions, action => action.Id == ShaclActions.RemoveShapeActionId);
    }

    [Fact]
    public void TheCases_StaySilentForAnotherReadingsOrigin()
    {
        // The rule that needs enforcing precisely because one selection vocabulary is shared
        // across the family: the same element id under the anchor's origin is not this
        // reading's business, and a shapes menu must not appear on a data-graph diagram.
        Assert.Empty(ShaclActions.Discover(Entry(), Target(CardId, ServiceCollectionAddRdfExtension.RdfOrigin)));
        Assert.Empty(ShaclActions.Discover(Entry(), Target(CardId, ServiceCollectionAddSkosExtension.SkosOrigin)));
    }

    [Fact]
    public void ATargetChip_IsRemovableByItsOwnAddress()
    {
        var groups = ShaclActions.Discover(Entry(), Target(CardId, ServiceCollectionAddShaclExtension.ShaclOrigin));

        // One entry per declared target, its id carrying the predicate and term the chip holds -
        // so the chip is removable although it is not a selectable element.
        var remove = Assert.Single(
            Assert.Single(groups).Actions,
            action => action.Id.StartsWith(ShaclActions.RemoveTargetActionIdPrefix, StringComparison.Ordinal));

        Assert.Equal(
            ShaclActions.RemoveTargetActionIdPrefix + ShaclVocabulary.TargetClass + "|http://example.org/Person",
            remove.Id);
        Assert.Contains("class ex:Person", remove.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRemoveEntry_StatesTheCountBeforeAnythingRuns()
    {
        var groups = ShaclActions.Discover(Entry(), Target(CardId, ServiceCollectionAddShaclExtension.ShaclOrigin));

        var remove = Assert.Single(Assert.Single(groups).Actions, action => action.Id == ShaclActions.RemoveShapeActionId);

        // The shape's three triples plus its blank subtree's two.
        Assert.Contains("with 5 statements", remove.Label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlankRootedSelection_IsMarkedUnavailable_AndRefusedOnExecute()
    {
        var body = IoPath.Combine(_root, "anon.ttl");
        await File.WriteAllTextAsync(body, "@prefix sh: <http://www.w3.org/ns/shacl#> .\r\n@prefix ex: <http://example.org/> .\r\n\r\nex:S a sh:NodeShape ; sh:node [ sh:closed true ] .\r\n", TestContext.Current.CancellationToken);
        var entry = _provider.GetRequiredService<IRdfDocumentStore>().GetOrLoad(body);
        var blankId = ShaclProjection.Project(entry.Model).Cards.Single(card => card.Blank).Id;
        var target = new ContextTarget(ContextScope.DiagramElement, body, false, ShortGuid.NewShortGuid(), _root, default, blankId)
        {
            Origin = ServiceCollectionAddShaclExtension.ShaclOrigin,
        };

        // Discovery marks it unavailable with the reason rather than hiding it.
        var actions = Assert.Single(ShaclActions.Discover(entry, target)).Actions;
        Assert.All(actions, action =>
        {
            ArgumentNullException.ThrowIfNull(action);
            Assert.False(action.Available);
            Assert.Equal(ShaclRefusals.BlankRooted, action.UnavailableReason);
        });

        // And executing anyway answers with the same sentence, decided without the writer.
        var result = await ShaclActions.ExecuteAsync(
            _provider.GetRequiredService<IHistoryStackStore>(), entry, target,
            ShaclActions.DeactivateActionId, TestContext.Current.CancellationToken);

        var failed = Assert.IsType<ContextExecutionFailed>(result);
        Assert.Equal(ShaclRefusals.BlankRooted, failed.Message);
    }

    [Fact]
    public async Task AddingATarget_AsksForATerm_ThenCommitsItAsOneUndoableEdit()
    {
        var target = Target(CardId, ServiceCollectionAddShaclExtension.ShaclOrigin);
        var history = _provider.GetRequiredService<IHistoryStackStore>();

        var asked = await ShaclActions.ExecuteAsync(history, Entry(), target, ShaclActions.AddTargetClassActionId, TestContext.Current.CancellationToken);
        Assert.IsType<ContextExecutionRequiresInput>(asked);

        // An undeclared prefix is refused by name before any splice.
        Assert.False(ShaclActions.Validate(Entry(), ShaclActions.AddTargetClassActionId, "nope:Thing")!.Valid);

        var command = ShaclActions.CommandFor(Entry(), target, ShaclActions.AddTargetClassActionId, "ex:Employee");
        var add = Assert.IsType<AddShaclTargetCommand>(command);
        var result = await history.Get(_root).ExecuteAsync(add, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("sh:targetClass ex:Employee", await File.ReadAllTextAsync(_body, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovingATargetByItsChipAddress_TakesExactlyThatDeclaration()
    {
        var target = Target(CardId, ServiceCollectionAddShaclExtension.ShaclOrigin);
        var history = _provider.GetRequiredService<IHistoryStackStore>();

        var result = await ShaclActions.ExecuteAsync(
            history, Entry(), target,
            ShaclActions.RemoveTargetActionIdPrefix + ShaclVocabulary.TargetClass + "|http://example.org/Person",
            TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionCompleted>(result);
        var text = await File.ReadAllTextAsync(_body, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("sh:targetClass", text, StringComparison.Ordinal);
        Assert.Contains("sh:property [ sh:path ex:name", text, StringComparison.Ordinal); // untouched
    }

    [Fact]
    public void ThePalette_OffersTheTwoEntriesTheMenuAlsoRuns()
    {
        var toolbox = _provider.GetServices<IDiagramToolboxProvider>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddShaclExtension.ShaclOrigin);

        // Each entry names an action the menu offers too, so a drop and a menu click cannot
        // disagree about what happens.
        Assert.Equal(
            [ShaclActions.AddNodeShapeActionId, ShaclActions.AddPropertyRowActionId],
            toolbox.Items.Select(item => item.DropActionId));
    }
}
