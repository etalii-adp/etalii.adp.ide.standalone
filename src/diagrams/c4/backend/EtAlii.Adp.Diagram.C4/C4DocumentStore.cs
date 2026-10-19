using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.C4;

/// <inheritdoc cref="IC4DocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store, told how a C4 workspace reads (code-structure FR-029).</b> Opening, the
/// retries before a refused read is believed, keeping the last good model through a reload that
/// cannot read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own
/// save (R2.3), refusing to write an entry standing in for a body that could not be read, and
/// telling the sessions are all <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s. What stays
/// here is what is this module's: how a body's text becomes a document and a workspace, and the
/// re-delivery a drag asks for.
/// </para>
/// <para>
/// <b>Every text reads.</b> The parser passes over what it cannot read and the validator reports it,
/// so no body of this type is unreadable for what it says - only for a read that failed.
/// </para>
/// </remarks>
public sealed class C4DocumentStore(IFileWriter writer) : LifecycleDocumentStore<LineDocument, C4Workspace>(writer), IC4DocumentStore
{
    /// <inheritdoc />
    public async Task TouchAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await GetOrLoadAsync(path, cancellationToken);
        OnChanged(path);
    }

    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;

    /// <inheritdoc />
    protected override ParseOutcome<C4Workspace> Parse(LineDocument body) => new(C4Parser.Parse(body), []);
}
