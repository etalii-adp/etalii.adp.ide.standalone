using System.Collections.Concurrent;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <inheritdoc cref="IWardleyDocumentStore" />
/// <remarks>
/// <para>
/// <b>A thin use of the shared lifecycle (backend-centralization task 6).</b> Opening, the retries
/// before a refused read is believed, keeping the last good document through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5) and ignoring this store's own save
/// (R2.3), the atomic publish (Requirement 3.6) and creating the folder a first save needs, are all
/// <see cref="WritableDocumentLifecycle{TDocument}"/>'s. What stays here is what is this module's: how
/// a body's text becomes a document, the identities held beside it and their sidecar, and telling
/// the sessions.
/// </para>
/// <para>
/// <b>The sessions hear about a reload only when the lifecycle installed a document</b>, and only
/// then are the identities re-reconciled. One that kept the last good document changed nothing a
/// session shows, so the identities it was holding still match it.
/// </para>
/// </remarks>
public sealed class WardleyDocumentStore : IWardleyDocumentStore
{
    // A first open that cannot read opens as empty with a warning naming the path, which is the
    // lifecycle's own default and what this store did before it (Requirement 2.4, R2.2) - so no
    // unavailable document is declared here.
    private readonly WritableDocumentLifecycle<WardleyDocument> _lifecycle = new(
        (_, text) => WardleyDocument.Parse(text),
        document => document.ToText());

    private readonly ConcurrentDictionary<string, IReadOnlyList<WardleyIdentityEntry>> _identities =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly WardleyIdentities _sidecar = new();

    public event EventHandler<WardleyDocumentChangedEventArgs>? Changed;

    public WardleyDocument GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    public IReadOnlyList<WardleyIdentityEntry> Identities(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _identities.GetOrAdd(
            path,
            key => WardleyIdentities.Reconcile(WardleyParser.Parse(GetOrLoad(key)), _sidecar.Read(key)));
    }

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

    public WardleyPublishResult Save(string path, WardleyDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        // The lifecycle writes the caller's document and caches exactly that object, whether or
        // not the write lands - never whatever the cache held at this moment.
        var result = _lifecycle.Save(path, document);
        if (result.Failed)
        {
            // Reported, not swallowed. This was once a bare `return` out of a `void` method, so a
            // publish that could not land - an editor holding the file, a transient sharing
            // conflict - was invisible to the caller, which then answered the user with success
            // while the document on disk still held the old position. What WardleyMapFlowTests
            // caught intermittently was not a flaky test: a failed save answered as a successful
            // one. The identities and their sidecar are left as they were, as is the session.
            return WardleyPublishResult.Failed(result.Error);
        }

        // The edit may have added or removed elements, so identities are re-reconciled against
        // what was already assigned - anything that survived keeps its id - and only now written.
        // A map opened and never edited reaches none of this (Requirement 4.3).
        var reconciled = WardleyIdentities.Reconcile(WardleyParser.Parse(document), Identities(path));
        _identities[path] = reconciled;
        var warning = _sidecar.Write(path, reconciled);

        Changed?.Invoke(this, new WardleyDocumentChangedEventArgs(path));
        return WardleyPublishResult.WithWarning(warning);
    }

    public void Touch(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Changed?.Invoke(this, new WardleyDocumentChangedEventArgs(path));
    }

    public void Forget(string path)
    {
        _lifecycle.Forget(path);
        _identities.TryRemove(path, out _);
    }

    public void Reload(string path)
    {
        var previous = Known(path);
        if (_lifecycle.Reload(path))
        {
            Installed(path, previous);
        }
    }

    public void BodyDeleted(string path)
    {
        var previous = Known(path);
        if (_lifecycle.BodyDeleted(path))
        {
            Installed(path, previous);
        }
    }

    private IReadOnlyList<WardleyIdentityEntry> Known(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _identities.TryGetValue(path, out var known) ? known : [];
    }

    private void Installed(string path, IReadOnlyList<WardleyIdentityEntry> previous)
    {
        // Reconciled against what this process already assigned rather than against the sidecar
        // alone: an element that survived an external edit keeps the id its connections are
        // already holding, so a `git pull` does not silently reset every selection.
        _identities[path] = WardleyIdentities.Reconcile(WardleyParser.Parse(_lifecycle.GetOrLoad(path)), previous);

        Changed?.Invoke(this, new WardleyDocumentChangedEventArgs(path));
    }
}
