namespace EtAlii.Adp.Documents;

/// <summary>
/// A store whose edits are saved against the file as it is now rather than as it was read: for a
/// body other programs write while it is open, as agents write an activity file.
/// </summary>
/// <remarks>
/// <see cref="LifecycleDocumentStore{TBody, TModel}"/> offers the save; a module whose edits need it
/// declares its store interface as this one, and its handlers run through
/// <c>DocumentEditRun.RunOnCurrent</c>.
/// </remarks>
public interface IEditSavingDocumentStore<TBody, TModel> : IDiagramDocumentStore<TBody, TModel>
{
    /// <inheritdoc cref="LifecycleDocumentStore{TBody, TModel}.Save(string, DocumentEntry{TBody, TModel}, Func{TBody, string})"/>
    DocumentEditSaved Save(string path, DocumentEntry<TBody, TModel> basis, Func<TBody, string> edit);
}
