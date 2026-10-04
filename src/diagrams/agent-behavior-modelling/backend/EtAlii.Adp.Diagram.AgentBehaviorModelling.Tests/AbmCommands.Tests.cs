using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>
/// Every command edits, every undo gives back the original bytes, and every refusal writes nothing -
/// on the pull-request-reviewer example, through the real store on a real file.
/// </summary>
public sealed class AbmCommandsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.AbmCommandsTests", Guid.NewGuid().ToString("N"));
    private readonly AbmDocumentStore _store = new();
    private readonly AbmTestDispatcher _dispatcher;
    private readonly byte[] _original;

    public AbmCommandsTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(AbmExamples.BodyOf("pull-request-reviewer"), Body);
        _original = File.ReadAllBytes(Body);
        _dispatcher = new AbmTestDispatcher(_store);
    }

    private string Body => Path.Combine(_folder, "reviewer.md");

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

    public static TheoryData<string> EveryEdit =>
    [
        "add a child", "add a root", "remove a subtree", "rename", "change the kind", "set a retry's attempts",
        "add notes", "remove notes", "move earlier", "move under another parent",
    ];

    // The example: 1 Try in order > 1.1 Do in order (skip) > 1.1.1 Check, 1.1.2 Do;
    // 1.2 Do in order (review) > 1.2.1 Do together > 1.2.1.1-3 Do; 1.2.2 Retry > 1.2.2.1 Do; 1.2.3 Do; 1.2.4 approval > 1.2.4.1 Do.
    private ICommand EditNamed(string name) => name switch
    {
        "add a child" => new AddAbmNodeCommand(Body, AbmNodeKinds.Check, "1.2", 0),
        "add a root" => new AddAbmNodeCommand(Body, AbmNodeKinds.Action, "", -1, "Say goodbye"),
        "remove a subtree" => new RemoveAbmNodeCommand(Body, "1.2.1"),
        "rename" => new RenameAbmNodeCommand(Body, "1.2.3", "Write the review comments"),
        "change the kind" => new SetAbmNodeKindCommand(Body, "1.2.1", AbmNodeKinds.Sequence),
        "set a retry's attempts" => new SetAbmNodeKindCommand(Body, "1.2.2", AbmNodeKinds.Retry, 5),
        "add notes" => new SetAbmNotesCommand(Body, "1.1.1", "Generated files are listed in .gitattributes."),
        "remove notes" => new SetAbmNotesCommand(Body, "1.2.3", ""),
        "move earlier" => new MoveAbmNodeCommand(Body, "1.2", "1", 0),
        "move under another parent" => new MoveAbmNodeCommand(Body, "1.2.3", "1.2.1", -1),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such edit."),
    };

    [Theory]
    [MemberData(nameof(EveryEdit))]
    public async Task EveryEdit_ThenItsUndo_GivesTheOriginalBytes(string edit)
    {
        // Act.
        var result = await _dispatcher.DispatchAsync(EditNamed(edit), TestContext.Current.CancellationToken);

        // Assert: it edited - otherwise the undo proves nothing.
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));

        // Act: the undo.
        var undone = await _dispatcher.DispatchAsync(Assert.IsAssignableFrom<ICommand>(result.Inverse), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    public static TheoryData<string, string> EveryRefusal => new()
    {
        { "add a child under a leaf", "holds no children" },
        { "add a second child under a wrapper", "exactly one child" },
        { "turn a parent into a leaf", "holds no children" },
        { "move a node beneath itself", "beneath itself" },
        { "rename a node that is not there", "no longer in this behavior model" },
    };

    private ICommand RefusalNamed(string name) => name switch
    {
        "add a child under a leaf" => new AddAbmNodeCommand(Body, AbmNodeKinds.Action, "1.1.1", -1),
        "add a second child under a wrapper" => new AddAbmNodeCommand(Body, AbmNodeKinds.Action, "1.2.2", -1),
        "turn a parent into a leaf" => new SetAbmNodeKindCommand(Body, "1.2", AbmNodeKinds.Check),
        "move a node beneath itself" => new MoveAbmNodeCommand(Body, "1.2", "1.2.1", -1),
        "rename a node that is not there" => new RenameAbmNodeCommand(Body, "9.9", "x"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such refusal."),
    };

    [Theory]
    [MemberData(nameof(EveryRefusal))]
    public async Task EveryRefusal_WritesNothing_AndSaysWhy(string refusal, string saying)
    {
        // Arrange: the cache is loaded, so an in-place edit would have somewhere to hide.
        var cachedBefore = _store.GetOrLoad(Body).Document.Text;

        // Act.
        var result = await _dispatcher.DispatchAsync(RefusalNamed(refusal), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains(saying, result.Error, StringComparison.Ordinal);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Equal(cachedBefore, _store.GetOrLoad(Body).Document.Text);
    }
}
