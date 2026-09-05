namespace EtAlii.Adp.Backend.Context;

/// <summary>Puts a command's warning on the context stream of every watcher of that project.</summary>
internal sealed class ContextNoticeSink : IContextNoticeSink
{
    private readonly IContextSelectionStore _selectionStore;

    public ContextNoticeSink(IContextSelectionStore selectionStore)
    {
        ArgumentNullException.ThrowIfNull(selectionStore);
        _selectionStore = selectionStore;
    }

    public void Notify(string rootPath, string message) => _selectionStore.PushNotice(rootPath, message);
}
