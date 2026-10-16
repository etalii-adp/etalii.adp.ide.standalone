using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// The open activity files, one per path, shared by every connection viewing one.
/// </summary>
/// <remarks>
/// <b>A marker.</b> Agents write an activity file while it is open, so every command saves its edit
/// against the file as it is now (Requirement 8.6), which <see cref="IEditSavingDocumentStore{TBody, TModel}"/>
/// declares and the shared store offers.
/// </remarks>
public interface IAadDocumentStore : IEditSavingDocumentStore<AadBody, AadModel>;

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
    protected override ParseOutcome<AadModel> Parse(AadBody body) => new(AadModel.Read(body), []);

    /// <inheritdoc />
    protected override string UnreadableOf(AadBody body) => body.Unreadable;
}
