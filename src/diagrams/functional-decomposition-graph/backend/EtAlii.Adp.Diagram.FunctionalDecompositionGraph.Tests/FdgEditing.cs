using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// Runs a command through its handler on a text held in memory, as the store would give it, and says what the
/// handler saved or why it refused: the product's way of editing, with no file under it.
/// </summary>
internal static class FdgEditing
{
    /// <summary>What <paramref name="command"/> does to <paramref name="text"/>: the saved text (the same text when nothing was saved), or the refusal.</summary>
    public static async Task<(string? Text, string? Refusal)> RunAsync(string text, ICommand command, CancellationToken cancellationToken)
    {
        var store = new Store(text);
        var result = await Handle(store, command, cancellationToken);
        return result.IsSuccess ? (store.Saved ?? text, null) : (null, result.Error);
    }

    private static Task<CommandResult> Handle(Store store, ICommand command, CancellationToken cancellationToken) => command switch
    {
        AddFdgElementCommand add => new AddFdgElementCommandHandler(store).ExecuteAsync(add, cancellationToken),
        RemoveFdgElementCommand remove => new RemoveFdgElementCommandHandler(store).ExecuteAsync(remove, cancellationToken),
        SetFdgPlacementCommand move => new SetFdgPlacementCommandHandler(store).ExecuteAsync(move, cancellationToken),
        SetFdgSizeCommand size => new SetFdgSizeCommandHandler(store).ExecuteAsync(size, cancellationToken),
        ConnectFdgElementsCommand connect => new ConnectFdgElementsCommandHandler(store).ExecuteAsync(connect, cancellationToken),
        DisconnectFdgConnectionCommand disconnect => new DisconnectFdgConnectionCommandHandler(store).ExecuteAsync(disconnect, cancellationToken),
        RenameFdgElementCommand rename => new RenameFdgElementCommandHandler(store).ExecuteAsync(rename, cancellationToken),
        SetFdgDescriptionCommand describe => new SetFdgDescriptionCommandHandler(store).ExecuteAsync(describe, cancellationToken),
        RenameFdgConnectionCommand rename => new RenameFdgConnectionCommandHandler(store).ExecuteAsync(rename, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "No handler for this command."),
    };

    /// <summary>A store holding one document that never changes; a save records the text it was given.</summary>
    private sealed class Store(string text) : IFdgDocumentStore
    {
        /// <summary>The text of the last save, or null.</summary>
        public string? Saved { get; private set; }

        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public ValueTask<DocumentEntry<FdgBody, DislModel>> GetOrLoadAsync(string path, CancellationToken cancellationToken) => ValueTask.FromResult(Get(path));

        public DocumentEntry<FdgBody, DislModel> Get(string path)
        {
            var body = FdgBody.Parse(text);
            return new DocumentEntry<FdgBody, DislModel>(body, body.Disl);
        }

        public Task<DocumentSaveResult> SaveAsync(string path, FdgBody document, CancellationToken cancellationToken)
        {
            Saved = document.Text;
            return Task.FromResult(DocumentSaveResult.Ok);
        }

        public void Forget(string path)
        {
        }

        public Task BodyDeletedAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReloadAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
