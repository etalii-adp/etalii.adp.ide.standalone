using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>A session follows both files: the Markdown through the store, the registration's positions through the history.</summary>
public sealed class AbmSessionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.AbmSessionTests", Guid.NewGuid().ToString("N"));
    private readonly AbmDocumentStore _store = new();

    public AbmSessionTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(AbmExamples.BodyOf("bug-fixer"), Body);
        File.WriteAllText(Registration, "etalii/agent-behavior-modelling\r\n");
    }

    private string Body => Path.Combine(_folder, "agent.md");

    private string Registration => Path.Combine(_folder, "agent.adp");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is not a test failure.
        }
    }

    [Fact]
    public async Task ADrag_IsStoredInTheRegistration_PushedAsADelta_AndUndone()
    {
        // Arrange.
        var history = new HistoryStack(new AbmTestDispatcher(_store));
        await using var session = new AbmSession(Body, Registration, _store, new AbmElementMapper(), history);
        session.Baseline();
        var markdown = await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken);
        var pushed = new List<DiagramDelta>();
        session.Changed += (_, args) => pushed.AddRange(args.Deltas);

        // Act.
        var refusal = await session.MoveElementToAsync("1.2", 500, 700, TestContext.Current.CancellationToken);

        // Assert: the position is in the registration, the Markdown did not change by a byte, and the move was pushed.
        Assert.Equal("", refusal);
        Assert.Equal(new RegistrationPosition(500, 700), RegistrationLayout.Read(Registration)["1.2"]);
        Assert.Equal(markdown, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        var moved = Assert.IsType<DiagramAddDelta>(Assert.Single(pushed));
        Assert.Equal(500 + (AbmLayout.NodeWidth / 2), Assert.Single(moved.Elements, element => element.Id == "1.2").X);

        // Act: undo.
        await history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(RegistrationLayout.Read(Registration).ContainsKey("1.2"));
    }

    [Fact]
    public async Task AnEdit_IsPushedToTheSession()
    {
        // Arrange.
        var history = new HistoryStack(new AbmTestDispatcher(_store));
        await using var session = new AbmSession(Body, Registration, _store, new AbmElementMapper(), history);
        session.Baseline();
        var pushed = new List<DiagramDelta>();
        session.Changed += (_, args) => pushed.AddRange(args.Deltas);

        // Act.
        var result = await history.ExecuteAsync(new RenameAbmNodeCommand(Body, "1.1", "Read the report"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains(pushed.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements), element => element.Id == "1.1");
    }

    [Fact]
    public async Task ADragWithoutARegistration_IsRefused()
    {
        // Arrange.
        var history = new HistoryStack(new AbmTestDispatcher(_store));
        await using var session = new AbmSession(Body, null, _store, new AbmElementMapper(), history);

        // Act.
        var refusal = await session.MoveElementToAsync("1", 0, 0, TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotEqual("", refusal);
    }
}
