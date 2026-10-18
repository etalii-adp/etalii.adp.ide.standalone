using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// The behavior model store: the shared store, told how a Markdown body reads through its binding
/// and what it means.
/// </summary>
public sealed class AbmDocumentStore(IFileWriter writer) : LifecycleDocumentStore<AbmBody, AbmModel>(writer), IAbmDocumentStore
{
    /// <inheritdoc />
    protected override AbmBody Read(string text) => AbmBody.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(AbmBody body) => body.Text;

    /// <inheritdoc />
    protected override ParseOutcome<AbmModel> Parse(AbmBody body) => new(body.Model, []);
}
