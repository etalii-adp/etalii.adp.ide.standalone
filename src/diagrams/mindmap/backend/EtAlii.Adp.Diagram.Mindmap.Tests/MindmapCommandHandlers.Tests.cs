using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// What every mindmap command handler does with a save (code-structure FR-022): a warning of the save
/// reaches the command's result. The shared <c>EditHandlerContract</c> holds the modules on the edit
/// base to this; the mindmap's store is not a body-and-model store, so its handlers are held to it here.
/// </summary>
public sealed class MindmapCommandHandlersTests : IDisposable
{
    private const string SaveWarning = "The handlers' test warning: something beside the map was not kept.";

    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.MindmapCommandHandlersTests");
    private readonly ServiceProvider _services;

    public MindmapCommandHandlersTests()
    {
        Body = _folder.Combine("architecture.mm");
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "architecture.mm"), Body);
        _services = new ServiceCollection()
            .AddSingleton<IMindmapDocumentStore>(new WarningStore(new MindmapDocumentStore(new FileWriter()), SaveWarning))
            .AddCommands()
            .AddMindmapCommands()
            .BuildServiceProvider();
    }

    private string Body { get; }

    public static TheoryData<string> Commands() => ["add-child", "add-sibling", "move", "remove", "text", "notes", "link"];

    public void Dispose()
    {
        _services.Dispose();
        _folder.Dispose();
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task AWarningOfTheSave_ReachesTheCommandResult(string name)
    {
        // Act.
        var result = await DispatchAsync(CommandOf(name));

        // Assert: the edit landed, can be undone, and says what did not.
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Inverse);
        Assert.Equal(SaveWarning, result.Warning);
    }

    [Fact]
    public async Task AWarningOfTheSave_ReachesTheResultOfARestore()
    {
        // Arrange: a removal, whose undo is a restore.
        var removed = await DispatchAsync(new RemoveNodeCommand(Body, "ID_88117421"));

        // Act.
        var result = await DispatchAsync(removed.Inverse!);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SaveWarning, result.Warning);
    }

    private ICommand CommandOf(string name) => name switch
    {
        "add-child" => new AddChildNodeCommand(Body, "ID_411002937", "ID_new00001", "New"),
        "add-sibling" => new AddSiblingNodeCommand(Body, "ID_88117420", "ID_new00002", "New"),
        "move" => new MoveNodeCommand(Body, "ID_88117422", "ID_411002937", 0),
        "remove" => new RemoveNodeCommand(Body, "ID_88117420"),
        "text" => new SetNodeTextCommand(Body, "ID_88117420", "Renamed"),
        "notes" => new SetNodeNotesCommand(Body, "ID_88117420", "Noted"),
        "link" => new SetNodeLinkCommand(Body, "ID_88117420", "https://example.org/"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private Task<CommandResult> DispatchAsync(ICommand command) =>
        _services.GetRequiredService<ICommandDispatcher>().DispatchAsync(command, TestContext.Current.CancellationToken);

    /// <summary>The module's store, with every save that lands carrying <paramref name="warning"/>.</summary>
    private sealed class WarningStore(IMindmapDocumentStore inner, string warning) : IMindmapDocumentStore
    {
        public event EventHandler<MindmapChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public MindmapDocument GetOrLoad(string bodyPath) => inner.GetOrLoad(bodyPath);

        public ValueTask<MindmapDocument> GetOrLoadAsync(string bodyPath, CancellationToken cancellationToken) => inner.GetOrLoadAsync(bodyPath, cancellationToken);

        public MindmapDocument? Get(string bodyPath) => inner.Get(bodyPath);

        public async Task<DocumentSaveResult> SaveAsync(string bodyPath, MindmapDocument document, MindmapChange change, CancellationToken cancellationToken)
        {
            var saved = await inner.SaveAsync(bodyPath, document, change, cancellationToken);
            return saved.Failed ? saved : DocumentSaveResult.WithWarning(warning);
        }

        public void Reload(string path) => inner.Reload(path);

        public void BodyDeleted(string path) => inner.BodyDeleted(path);

        public void Forget(string path) => inner.Forget(path);
    }
}
