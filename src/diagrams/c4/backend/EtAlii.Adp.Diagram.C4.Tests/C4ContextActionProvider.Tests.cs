using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
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
        new(ContextScope.DiagramElement, _bodyPath, IsContainer: false, SourceId: default, _root, ShortGuid.NewShortGuid(), elementId);

    /// <summary>Executes an action that is expected to ask for a value, and hands back the request.</summary>
    private async Task<ContextExecutionRequiresInput> Input(ContextTarget target, string actionId) =>
        Assert.IsType<ContextExecutionRequiresInput>(await _provider.ExecuteAsync(target, actionId, TestContext.Current.CancellationToken));

    private async Task<string[]> ActionIdsFor(string elementId)
    {
        var groups = await _provider.DiscoverAsync(TargetFor(elementId), TestContext.Current.CancellationToken);
        return groups.SelectMany(group => group.Actions).Select(action => action.Id).ToArray();
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
        var relationshipId = _documents.WorkspaceOf(_bodyPath).Relationships.Single().Id;

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
        var relationshipId = _documents.WorkspaceOf(_bodyPath).Relationships.Single().Id;

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
        var relationship = _documents.WorkspaceOf(_bodyPath).Relationships.Single();
        Assert.NotEqual("", relationship.Technology);

        // Act.
        var result = await _provider.CommitAsync(
            TargetFor(relationship.Id), C4ContextActionProvider.RelabelActionId, "Views accounts using", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Completed, result.Error);
        var after = _documents.WorkspaceOf(_bodyPath).Relationships.Single();
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
        var relationshipId = _documents.WorkspaceOf(_bodyPath).Relationships.Single().Id;

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
        var relationshipId = _documents.WorkspaceOf(_bodyPath).Relationships.Single().Id;

        var result = await _provider.ExecuteAsync(
            TargetFor(relationshipId), C4ContextActionProvider.SetTechnologyActionId, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var failed = Assert.IsType<ContextExecutionFailed>(result);
        Assert.Contains("does not apply", failed.Message, StringComparison.OrdinalIgnoreCase);
    }
}
