using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>What a drag on an activity file does: a drop locks the element where it was let go, and nothing has a parent to move it under.</summary>
public sealed class AadSessionTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("aad-session");
    private readonly string _root;
    private readonly ServiceProvider _services = Services();

    public AadSessionTests()
    {
        _root = _testFolder.Path;
    }

    private string Body => Path.Combine(_root, "work.aad");

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection().AddCommands();
        services.AddAgentActivityDiagram();
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _testFolder.Dispose();
    }

    private AadSession Open() => new(
        Body,
        _services.GetRequiredService<IAadDocumentStore>(),
        new AadElementMapper(),
        _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_root)));

    [Fact]
    public async Task ADrop_LocksTheElementWhereItWasLetGo()
    {
        // Arrange.
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Body, AadDocumentTests.Fixture("one-day.aad"), token);
        await using var session = Open();

        // Act: an element the layout places, dropped somewhere.
        var refusal = await session.MoveElementToAsync("a-dev2", 120, 40, token);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Contains("    - element: a-dev2\r\n      x: 120\r\n      y: 40\r\n", await File.ReadAllTextAsync(Body, token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADragOntoAnotherElement_IsRefused_InThisDiagramsWords()
    {
        // Arrange.
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Body, AadDocumentTests.Fixture("one-day.aad"), token);
        await using var session = Open();

        // Act.
        var refusal = await session.MoveElementAsync("a-dev2", "s-knowledge", 0, token);

        // Assert.
        Assert.Equal("An element of this diagram has no parent to move it under; it is locked where it is dropped.", refusal);
    }
}
