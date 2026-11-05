using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// A toolbox entry dropped on a C4 element, over the real host, sent as the canvas sends it: the
/// entry's drop action on the element it landed on, naming the diagram. No C4 element menu has an
/// add, so a drop resolved by the target's menu found nothing and every drop on an element was
/// refused with "That action is not available for this item.".
/// </summary>
public class C4DropFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private const string Model = """
        workspace "Bank" {
            model {
                u = person "Customer" "A customer."
                s = softwareSystem "Banking" "Does banking."
                u -> s "Uses"
            }
            views {
                container s "containers" {
                    include *
                }
            }
        }
        """;

    private readonly string _projectFolder = Seed(host.NewProjectFolder("c4-drop"));

    private static string Seed(string projectFolder)
    {
        File.WriteAllText(IoPath.Combine(projectFolder, "banking.dsl"), Model);
        File.WriteAllText(IoPath.Combine(projectFolder, "containers.adp"), "c4/container\nbody: banking.dsl\nview: containers\n");
        return projectFolder;
    }

    [Fact]
    public async Task AContainerDroppedOnASoftwareSystem_AsksForItsName_AndIsAddedInsideIt()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var watchId = ShortGuid.NewShortGuid();
        var entryId = await NestedEntryLookup.EntryIdOfAsync(session.Hierarchy, session.ProjectId, watchId, session.Headers, "containers.adp");
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = contextCall.ResponseStream.ReadUntilPromptAsync(cts.Token);

        // Act.
        var dropped = await DropAsync(session, watchId, entryId, "s", "c4.add-container");
        Assert.True(dropped.Accepted, dropped.Error);
        var prompt = await pendingPrompt;
        var submitted = await session.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = prompt.InteractionId, Value = "Api" },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Name", prompt.InputDialog.FieldLabel);
        Assert.True(submitted.Completed, submitted.Error);
        var body = await File.ReadAllTextAsync(IoPath.Combine(_projectFolder, "banking.dsl"), TestContext.Current.CancellationToken);
        Assert.Matches("""softwareSystem "Banking" "Does banking." \{\s*api = container "Api" """, body);
    }

    [Fact]
    public async Task AContainerDroppedOnAPerson_IsRefusedWithTheDefinitionsSentence()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var watchId = ShortGuid.NewShortGuid();
        var entryId = await NestedEntryLookup.EntryIdOfAsync(session.Hierarchy, session.ProjectId, watchId, session.Headers, "containers.adp");

        // Act.
        var dropped = await DropAsync(session, watchId, entryId, "u", "c4.add-container");

        // Assert.
        Assert.False(dropped.Accepted);
        Assert.Equal("A container lives inside a software system, not inside a person.", dropped.Error);
        Assert.Equal(Model, await File.ReadAllTextAsync(IoPath.Combine(_projectFolder, "banking.dsl"), TestContext.Current.CancellationToken));
    }

    /// <summary>A toolbox entry's drop on <paramref name="elementId"/> of the diagram <paramref name="entryId"/>, as the canvas sends it.</summary>
    private static async Task<ExecuteActionResponse> DropAsync(FlowSession session, ShortGuid watchId, ShortGuid entryId, string elementId, string dropActionId) =>
        await session.Context.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Source = new ContextSource { ElementId = new ElementId { Value = elementId }, DiagramEntryId = entryId },
                InteractionId = ShortGuid.NewShortGuid(),
                DropActionId = dropActionId,
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
}
