using System.Collections.Concurrent;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <inheritdoc cref="IWardleyDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store, told how a Wardley map reads (code-structure FR-029).</b> Opening, the
/// retries before a refused read is believed, keeping the last good document through a reload that
/// cannot read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own
/// save (R2.3), the atomic publish (Requirement 3.6), refusing to write an entry standing in for a
/// body that could not be read, and telling the sessions are all
/// <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s. What stays here is what is this module's:
/// how a body's text becomes a document and a map, and the identities held beside it with their
/// sidecar.
/// </para>
/// <para>
/// <b>The identities are re-reconciled only when a document was installed or saved</b>. A reload
/// that kept the last good document changed nothing a session shows, so the identities it was
/// holding still match it.
/// </para>
/// </remarks>
public sealed class WardleyDocumentStore : LifecycleDocumentStore<WardleyDocument, WardleyMap>, IWardleyDocumentStore
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<WardleyIdentityEntry>> _identities =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly WardleyIdentities _sidecar = new();

    /// <inheritdoc />
    public IReadOnlyList<WardleyIdentityEntry> Identities(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _identities.GetOrAdd(
            path,
            key => WardleyIdentities.Reconcile(GetOrLoad(key).Model, _sidecar.Read(key)));
    }

    /// <inheritdoc />
    public void Rekey(string path, string kind, string oldKey, string newKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(oldKey);
        ArgumentNullException.ThrowIfNull(newKey);

        // Every entry whose key mentions the old one moves with it, not just the element's own:
        // a link's key carries both endpoint names, so renaming a component rekeys the links
        // that reach it too, and those keep their identities as well.
        _identities[path] = Identities(path)
            .Select(entry => entry.Kind == kind && entry.Key == oldKey
                ? entry with { Key = newKey }
                : entry)
            .ToArray();
    }

    /// <summary>
    /// Tells every session on this document to re-deliver, without changing the document.
    /// </summary>
    public void Touch(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        OnChanged(path);
    }

    /// <inheritdoc />
    public override void Forget(string path)
    {
        base.Forget(path);
        _identities.TryRemove(path, out _);
    }

    /// <inheritdoc />
    protected override WardleyDocument Read(string text) => WardleyDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(WardleyDocument body) => body.ToText();

    /// <inheritdoc />
    protected override WardleyMap Rebuild(WardleyDocument body) => WardleyParser.Parse(body);

    /// <inheritdoc />
    /// <remarks>
    /// The edit may have added or removed elements, so identities are re-reconciled against what was
    /// already assigned - anything that survived keeps its id - and only now written. A map opened
    /// and never edited reaches none of this (Requirement 4.3). A sidecar that could not be written
    /// is the save's warning: the map landed, and the ids it hands out will not survive a restart.
    /// </remarks>
    protected override string Saved(string path, DocumentEntry<WardleyDocument, WardleyMap> entry)
    {
        var reconciled = WardleyIdentities.Reconcile(entry.Model, Identities(path));
        _identities[path] = reconciled;
        return _sidecar.Write(path, reconciled);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reconciled against what this process already assigned rather than against the sidecar
    /// alone: an element that survived an external edit keeps the id its connections are
    /// already holding, so a `git pull` does not silently reset every selection.
    /// </remarks>
    protected override void Installed(string path)
    {
        var known = _identities.TryGetValue(path, out var held) ? held : [];
        _identities[path] = WardleyIdentities.Reconcile(GetOrLoad(path).Model, known);
    }
}
