using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.C4.Tests;

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
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(_bodyPath, Model);

        _services = new ServiceCollection().AddCommands().AddC4().BuildServiceProvider();
        _documents = _services.GetRequiredService<IC4DocumentStore>();
        _provider = new C4ContextActionProvider(
            _services.GetRequiredService<IHistoryStackStore>(),
            _documents);
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ContextTarget TargetFor(string elementId) =>
        new(ContextScope.DiagramElement, _bodyPath, IsContainer: false, SourceId: default, _root, ShortGuid.NewShortGuid(), elementId);

    private async Task<string[]> ActionIdsFor(string elementId)
    {
        var groups = await _provider.DiscoverAsync(TargetFor(elementId), TestContext.Current.CancellationToken);
        return groups.SelectMany(group => group.Actions).Select(action => action.Id).ToArray();
    }

    [Fact]
    public async Task AContainer_IsOfferedATechnology()
    {
        var ids = await ActionIdsFor("web");

        Assert.Contains(C4ContextActionProvider.RenameActionId, ids);
        Assert.Contains(C4ContextActionProvider.EditDescriptionActionId, ids);
        Assert.Contains(C4ContextActionProvider.SetTechnologyActionId, ids);
    }

    [Fact]
    public async Task APerson_IsNotOfferedATechnology_BecauseC4GivesThemNone()
    {
        // Not offered rather than offered-and-refused: C4 asks for a technology on containers
        // and components, and a person simply has none.
        var ids = await ActionIdsFor("u");

        Assert.Contains(C4ContextActionProvider.RenameActionId, ids);
        Assert.DoesNotContain(C4ContextActionProvider.SetTechnologyActionId, ids);
    }

    [Fact]
    public async Task ARelationship_IsOfferedARelabelAndAProtocol_AndNotARename()
    {
        var relationshipId = _documents.WorkspaceOf(_bodyPath).Relationships.Single().Id;

        var ids = await ActionIdsFor(relationshipId);

        Assert.Equal(
            [C4ContextActionProvider.RelabelActionId, C4ContextActionProvider.SetProtocolActionId],
            ids);
    }

    [Fact]
    public async Task SomethingNoLongerInTheModel_IsOfferedNothing()
    {
        Assert.Empty(await ActionIdsFor("ghost"));
    }

    [Fact]
    public async Task Rename_AsksForTheNameStartingFromTheCurrentOne()
    {
        var result = await _provider.ExecuteAsync(TargetFor("web"), C4ContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);

        var input = Assert.IsType<ContextExecutionResult.RequiresInput>(result);
        Assert.Equal("Web", input.Request.InitialValue);
    }

    [Fact]
    public async Task Rename_CommitsThroughACommand_SoItIsOneUndoAway()
    {
        await _provider.CommitAsync(TargetFor("web"), C4ContextActionProvider.RenameActionId, "Web Application", "", TestContext.Current.CancellationToken);

        Assert.Contains("container \"Web Application\"", File.ReadAllText(_bodyPath), StringComparison.Ordinal);
        Assert.True(_services.GetRequiredService<IHistoryStackStore>().Get(_root).CanUndo);
    }

    [Fact]
    public async Task Relabel_CommitsThroughACommand()
    {
        var relationshipId = _documents.WorkspaceOf(_bodyPath).Relationships.Single().Id;

        var result = await _provider.CommitAsync(
            TargetFor(relationshipId), C4ContextActionProvider.RelabelActionId, "Views accounts using", "", TestContext.Current.CancellationToken);

        Assert.True(result.Completed, result.Error);
        Assert.Contains("\"Views accounts using\"", File.ReadAllText(_bodyPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyName_IsRefused_BecauseNothingWouldIdentifyTheElement()
    {
        var result = await _provider.ValidateAsync(TargetFor("web"), C4ContextActionProvider.RenameActionId, "   ", TestContext.Current.CancellationToken);

        Assert.False(result.Valid);
        Assert.Contains("needs a name", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnEmptyDescription_IsAccepted_BecauseAnIncompleteModelIsOrdinary()
    {
        // C4 asks for a description and C4RuleSet warns when there is none - but a model
        // mid-edit is routinely incomplete, so the rule guides rather than blocks
        // (Requirement 10.7).
        var result = await _provider.ValidateAsync(TargetFor("web"), C4ContextActionProvider.EditDescriptionActionId, "", TestContext.Current.CancellationToken);

        Assert.True(result.Valid);
    }

    [Fact]
    public async Task AnActionThatDoesNotApplyToTheSelection_IsRefusedWithAReason()
    {
        var relationshipId = _documents.WorkspaceOf(_bodyPath).Relationships.Single().Id;

        var result = await _provider.ExecuteAsync(
            TargetFor(relationshipId), C4ContextActionProvider.SetTechnologyActionId, TestContext.Current.CancellationToken);

        var failed = Assert.IsType<ContextExecutionResult.Failed>(result);
        Assert.Contains("does not apply", failed.Message, StringComparison.OrdinalIgnoreCase);
    }
}
