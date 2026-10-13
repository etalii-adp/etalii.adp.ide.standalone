using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// The open activity files, one per path, shared by every connection viewing one.
/// </summary>
/// <remarks>
/// <b>A marker, but for one save of its own.</b> Everything else a store does is declared by
/// <see cref="IDiagramDocumentStore{TBody, TModel}"/>. Agents write an activity file while it is open,
/// so every command saves its edit against the file as it is now (Requirement 8.6), which the shared
/// store offers and this interface declares.
/// </remarks>
public interface IAadDocumentStore : IDiagramDocumentStore<AadBody, AadModel>
{
    /// <inheritdoc cref="LifecycleDocumentStore{TBody, TModel}.Save(string, DocumentEntry{TBody, TModel}, Func{TBody, string})"/>
    DocumentEditSaved Save(string path, DocumentEntry<AadBody, AadModel> basis, Func<AadBody, string> edit);
}

/// <summary>
/// The activity file store: the shared store, told how a body reads through its binding, what it
/// means, and when the binding could not read it at all.
/// </summary>
/// <remarks>
/// A file the binding cannot read - not YAML, or not an activity file - is held as unreadable with
/// the reader's own sentence, and nothing is written to it until it can be read (Requirement 8.5).
/// </remarks>
public sealed class AadDocumentStore : LifecycleDocumentStore<AadBody, AadModel>, IAadDocumentStore
{
    /// <inheritdoc />
    protected override AadBody Read(string text) => AadBody.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(AadBody body) => body.Text;

    /// <inheritdoc />
    protected override AadModel Rebuild(AadBody body) => AadModel.Read(body);

    /// <inheritdoc />
    protected override string UnreadableOf(AadBody body) => body.Unreadable;
}
