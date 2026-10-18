using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.Databricks;

/// <inheritdoc cref="IDatabricksDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store (code-structure FR-029).</b> Loading, the retries, keeping the last good
/// document through a reload that cannot read, clearing it only on the watcher's delete, ignoring
/// this store's own save and telling the sessions are <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s.
/// What stays here is how a body's text becomes the family's three readings.
/// </para>
/// <para>
/// <b>A file that is not YAML is an ordinary state for a file somebody is editing</b>, so it is held
/// as unreadable with the parser's sentence, rather than thrown out of the store, and nothing is
/// written to it until it parses (Requirement 2.3).
/// </para>
/// </remarks>
public sealed class DatabricksDocumentStore(IFileWriter writer) : LifecycleDocumentStore<LineDocument, DatabricksModel>(writer), IDatabricksDocumentStore
{
    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;


    /// <inheritdoc />
    protected override ParseOutcome<DatabricksModel> Parse(LineDocument body) => DatabricksModel.Outcome(body);
}
