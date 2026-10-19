using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// What a user is offered on a C4 element or relationship, and what choosing it does. An action
/// that does not apply is not offered at all rather than greyed out
/// (c4-diagrams Requirements 13.3, 13.7).
/// </summary>
public class C4ContextActionProviderTests : IDisposable
{
    private readonly string _root;
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly C4ContextActionProvider _provider;
    private readonly IC4DocumentStore _documents;

    private const string Model = """
        workspace "Bank" {
            model {
                u = person "Customer" "A customer."
                s = softwareSystem "Banking" "Does banking." {
                    web = container "Web" "Serves." "React"
                }
                u -> web "Uses" "HTTPS"
            }
            views {
                container s "containers" {
                    include *
                }
            }
        }
        """;

    public C4ContextActionProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(_bodyPath, Model);

        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        _documents = _services.GetRequiredService<IC4DocumentStore>();
        _provider = new C4ContextActionProvider(
            _services.GetRequiredService<IHistoryStackStore>(),
            _documents);
    }

    public void Dispose()
    {
        _services.Dispose();
        TestFolder.TryDelete(_root);
    }

    private ContextTarget TargetFor(string elementId) =>
        ContextTarget.ForElement(_bodyPath, elementId, _root, ShortGuid.NewShortGuid());

    /// <summary>Executes an action that is expected to ask for a value, and hands back the request.</summary>
    private async Task<ContextExecutionRequiresInput> Input(ContextTarget target, string actionId) =>
        Assert.IsType<ContextExecutionRequiresInput>(await _provider.ExecuteAsync(target, actionId, TestContext.Current.CancellationToken));

    private async Task<IReadOnlyList<string>> ActionIdsFor(string elementId)
    {
        var groups = await _provider.DiscoverAsync(TargetFor(elementId), TestContext.Current.CancellationToken);
        return groups.ActionIds();
    }

    [Fact]
    public async Task AContainer_IsOfferedATechnology()
    {
        // Act.
        var ids = await ActionIdsFor("web");

        // Assert.
        Assert.Contains(C4ContextActionProvider.RenameActionId, ids);
        Assert.Contains(C4ContextActionProvider.EditDescriptionActionId, ids);
        Assert.Contains(C4ContextActionProvider.SetTechnologyActionId, ids);
    }

    [Fact]
    public async Task APerson_IsNotOfferedATechnology_BecauseC4GivesThemNone()
    {
        // Act.
        // Not offered rather than offered-and-refused: C4 asks for a technology on containers
        // and components, and a person simply has none.
        var ids = await ActionIdsFor("u");

        // Assert.
        Assert.Contains(C4ContextActionProvider.RenameActionId, ids);
        Assert.DoesNotContain(C4ContextActionProvider.SetTechnologyActionId, ids);
    }

    [Fact]
    public async Task ARelationship_IsOfferedARelabelAndAProtocol_AndNotARename()
    {
        // Arrange.
        var relationshipId = _documents.GetOrLoad(_bodyPath).Model.Relationships.Single().Id;

        // Act.
        var ids = await ActionIdsFor(relationshipId);

        // Assert.
        Assert.Equal(
            [C4ContextActionProvider.RelabelActionId, C4ContextActionProvider.SetProtocolActionId],
            ids);
    }

    [Fact]
    public async Task SomethingNoLongerInTheModel_IsOfferedNothing()
    {
        // Arrange, act and assert.
        Assert.Empty(await ActionIdsFor("ghost"));
    }

    [Fact]
    public async Task Rename_AsksForTheNameStartingFromTheCurrentOne()
    {
        // Arrange.
        var result = await _provider.ExecuteAsync(TargetFor("web"), C4ContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var input = Assert.IsType<ContextExecutionRequiresInput>(result);
        Assert.Equal("Web", input.Request.InitialValue);
    }

    [Fact]
    public async Task OnlyRenameAndRelabelMarkTheirPromptsAsEditingTheLabelOnScreen()
    {
        // Arrange.
        // C4 is the module where the line is finest. Five prompts ask for text about an element
        // or a relationship; two of them are asking for the words drawn on it, and three are
        // asking for something else about it. A technology and a protocol are not the label, and
        // an element's description is not drawn on its box at all
        // (inline-rename Requirements 3.1, 3.2).
        var relationshipId = _documents.GetOrLoad(_bodyPath).Model.Relationships.Single().Id;

        // Act.
        var rename = await Input(TargetFor("web"), C4ContextActionProvider.RenameActionId);
        var describe = await Input(TargetFor("web"), C4ContextActionProvider.EditDescriptionActionId);
        var technology = await Input(TargetFor("web"), C4ContextActionProvider.SetTechnologyActionId);
        var relabel = await Input(TargetFor(relationshipId), C4ContextActionProvider.RelabelActionId);
        var protocol = await Input(TargetFor(relationshipId), C4ContextActionProvider.SetProtocolActionId);

        // Assert.
        Assert.Equal("web", rename.Request.InlineLabelElementId);
        Assert.Equal(relationshipId, relabel.Request.InlineLabelElementId);
        Assert.Equal("", describe.Request.InlineLabelElementId);
        Assert.Equal("", technology.Request.InlineLabelElementId);
        Assert.Equal("", protocol.Request.InlineLabelElementId);
    }

    [Fact]
    public async Task Relabel_WritesOnlyTheDescription_LeavingTheTechnologyThatIsDrawnBesideIt()
    {
        // Arrange.
        // The canvas draws a relationship as its description followed by its technology in
        // brackets, so the editor replaces a label the user reads as one string. The defect this
        // catches is writing that whole rendered string back into the description - after which
        // the technology appears twice and the document says something nobody typed.
        var relationship = _documents.GetOrLoad(_bodyPath).Model.Relationships.Single();
        Assert.NotEqual("", relationship.Technology);

        // Act.
        var result = await _provider.CommitAsync(
            TargetFor(relationship.Id), C4ContextActionProvider.RelabelActionId, "Views accounts using", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Completed, result.Error);
        var after = _documents.GetOrLoad(_bodyPath).Model.Relationships.Single();
        Assert.Equal("Views accounts using", after.Description);
        Assert.Equal(relationship.Technology, after.Technology);
    }

    [Fact]
    public async Task Rename_CommitsThroughACommand_SoItIsOneUndoAway()
    {
        // Act.
        await _provider.CommitAsync(TargetFor("web"), C4ContextActionProvider.RenameActionId, "Web Application", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("container \"Web Application\"", await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.True(_services.GetRequiredService<IHistoryStackStore>().Get(_root).CanUndo);
    }

    [Fact]
    public async Task Relabel_CommitsThroughACommand()
    {
        // Arrange.
        var relationshipId = _documents.GetOrLoad(_bodyPath).Model.Relationships.Single().Id;

        // Act.
        var result = await _provider.CommitAsync(
            TargetFor(relationshipId), C4ContextActionProvider.RelabelActionId, "Views accounts using", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Contains("\"Views accounts using\"", await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyName_IsRefused_BecauseNothingWouldIdentifyTheElement()
    {
        // Act.
        var result = await _provider.ValidateAsync(TargetFor("web"), C4ContextActionProvider.RenameActionId, "   ", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Valid);
        Assert.Contains("needs a name", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnEmptyDescription_IsAccepted_BecauseAnIncompleteModelIsOrdinary()
    {
        // Act.
        // C4 asks for a description and C4RuleSet warns when there is none - but a model
        // mid-edit is routinely incomplete, so the rule guides rather than blocks
        // (Requirement 10.7).
        var result = await _provider.ValidateAsync(TargetFor("web"), C4ContextActionProvider.EditDescriptionActionId, "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Valid);
    }

    [Fact]
    public async Task AnActionThatDoesNotApplyToTheSelection_IsRefusedWithAReason()
    {
        // Arrange.
        var relationshipId = _documents.GetOrLoad(_bodyPath).Model.Relationships.Single().Id;

        var result = await _provider.ExecuteAsync(
            TargetFor(relationshipId), C4ContextActionProvider.SetTechnologyActionId, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var failed = Assert.IsType<ContextExecutionFailed>(result);
        Assert.Contains("does not apply", failed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("addPerson", "", "Auditor", "auditor")]
    [InlineData("addSoftwareSystem", "", "Web App", "webApp")]
    [InlineData("addContainer", "s", "Api", "api")]
    [InlineData("addComponent", "web", "Router", "router")]
    [InlineData("addContainer", "", "Nowhere", null)]
    [InlineData("addContainer", "u", "Wrong parent", null)]
    [InlineData("addPerson", "", "  ", null)]
    [InlineData("addContainer", "parity:unknown", "Orphan", null)]
    public void AnAdd_IsTheDefinitionsOperation_UnderTheIdentifierTheModuleMakesFromTheName(string operation, string parent, string name, string? expected)
    {
        // Arrange.
        var workspace = _documents.GetOrLoad(_bodyPath).Model;
        var identifier = C4Placement.IdentifierFor(workspace, name.Trim());

        // Act: the operation the commit runs first, handed that identifier (gap C11).
        var created = C4Definitions.Created(workspace, "model.dsl", C4Definitions.ViewOf(workspace, null), null, operation, parent, name, identifier);

        // Assert: it creates the element under that identifier where the definition allows the add, and
        // nothing where it refuses, which leaves the command to refuse on its own.
        Assert.Equal(expected, created);
    }

    [Fact]
    public async Task ADocumentThatCouldNotBeRead_IsRefusedByTheMenusAndTheGrid_AndWritesNothing()
    {
        // Arrange.
        var unreadable = new UnreadableStore(_documents);
        var historyStacks = _services.GetRequiredService<IHistoryStackStore>();
        var actions = new C4ContextActionProvider(historyStacks, unreadable);
        var properties = new C4ContextPropertyProvider(historyStacks, unreadable);
        var before = await File.ReadAllBytesAsync(_bodyPath, TestContext.Current.CancellationToken);

        // Act.
        var offered = await actions.DiscoverAsync(TargetFor(""), TestContext.Current.CancellationToken);
        var renamed = await actions.ExecuteAsync(TargetFor("web"), C4ContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);
        var committed = await actions.CommitAsync(TargetFor("web"), C4ContextActionProvider.RenameActionId, "Renamed", "", TestContext.Current.CancellationToken);
        var set = await properties.SetAsync(TargetFor("web"), C4ContextPropertyProvider.NamePropertyId, "Renamed", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(offered);
        Assert.Contains("could not be read", Assert.IsType<ContextExecutionFailed>(renamed).Message, StringComparison.Ordinal);
        Assert.False(committed.Completed);
        Assert.Contains("could not be read", committed.Error, StringComparison.Ordinal);
        Assert.False(set.IsSuccess);
        Assert.Contains("could not be read", set.Error, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(_bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileThatIsNotADslDocument_IsAnsweredWithNothing_WhateverItHolds()
    {
        // Arrange: the C4 model's very text, in a file no C4 type opens.
        var other = IoPath.Combine(_root, "model.txt");
        await File.WriteAllTextAsync(other, Model, TestContext.Current.CancellationToken);
        var target = ContextTarget.ForElement(other, "", _root, ShortGuid.NewShortGuid());
        var properties = new C4ContextPropertyProvider(_services.GetRequiredService<IHistoryStackStore>(), _documents);

        // Act.
        var offered = await _provider.DiscoverAsync(target, TestContext.Current.CancellationToken);
        var rows = await properties.DescribeAsync(ContextTarget.ForElement(other, "web", _root, ShortGuid.NewShortGuid()), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(offered);
        Assert.Empty(rows);
    }

    /// <summary>A store whose every document reads as one that could not be read.</summary>
    private sealed class UnreadableStore(IC4DocumentStore inner) : IC4DocumentStore
    {
        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public DocumentEntry<LineDocument, C4Workspace> GetOrLoad(string path) => inner.GetOrLoad(path) with { Unreadable = "it is locked" };

        public DocumentEntry<LineDocument, C4Workspace>? Get(string path) => inner.Get(path) is { } held ? held with { Unreadable = "it is locked" } : null;

        public Task<DocumentSaveResult> SaveAsync(string path, LineDocument document, CancellationToken cancellationToken) => inner.SaveAsync(path, document, cancellationToken);

        public void Reload(string path) => inner.Reload(path);

        public void BodyDeleted(string path) => inner.BodyDeleted(path);

        public void Forget(string path) => inner.Forget(path);

        public Task TouchAsync(string path, CancellationToken cancellationToken) => inner.TouchAsync(path, cancellationToken);
    }
}
