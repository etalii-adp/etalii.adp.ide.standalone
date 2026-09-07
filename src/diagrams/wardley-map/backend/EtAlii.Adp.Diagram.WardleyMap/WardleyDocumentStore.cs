using System.Collections.Concurrent;
using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <inheritdoc cref="IWardleyDocumentStore" />
public sealed class WardleyDocumentStore : IWardleyDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<WardleyDocumentStore>();

    private readonly ConcurrentDictionary<string, WardleyDocument> _documents = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, IReadOnlyList<WardleyIdentityEntry>> _identities =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly WardleyIdentities _sidecar = new();

    // The paths this store is writing right now, so its own save does not bounce back through
    // Reload as an "external" change - PlainEditorSession's saving guard, per path.
    private readonly ConcurrentDictionary<string, byte> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<WardleyDocumentChangedEventArgs>? Changed;

    public WardleyDocument GetOrLoad(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _documents.GetOrAdd(path, Load);
    }

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

    public WardleyPublishResult Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var document = GetOrLoad(path);
        string warning;
        _selfWrites[path] = 1;
        try
        {
            if (!WriteAtomically(path, document.ToText()))
            {
                // Reported, not swallowed. This was a bare `return` out of a `void` method, so a
                // publish that could not land - an editor holding the file, a transient sharing
                // conflict - was invisible to the caller, which then answered the user with
                // success while the document on disk still held the old position. Timeline has
                // reported this since it was written; wardley did not, and the difference is
                // what WardleyMapFlowTests caught intermittently. Not a flaky test: a failed
                // save answered as a successful one.
                return WardleyPublishResult.Failed($"{IoPath.GetFileName(path)} could not be written. The change is still here to try again.");
            }

            // The edit may have added or removed elements, so identities are re-reconciled
            // against what was already assigned - anything that survived keeps its id - and
            // only now written. A map opened and never edited reaches none of this
            // (Requirement 4.3).
            var reconciled = WardleyIdentities.Reconcile(WardleyParser.Parse(document), Identities(path));
            _identities[path] = reconciled;
            warning = _sidecar.Write(path, reconciled);
        }
        finally
        {
            _selfWrites.TryRemove(path, out _);
        }

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
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _documents.TryRemove(path, out _);
        _identities.TryRemove(path, out _);
    }

    public void Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_selfWrites.ContainsKey(path))
        {
            // The change on disk is this store's own save, mid-write; Save reconciles and
            // tells the sessions itself.
            return;
        }

        var previous = _identities.TryGetValue(path, out var known) ? known : [];
        _documents.TryRemove(path, out _);
        _identities.TryRemove(path, out _);

        // Reconciled against what this process already assigned rather than against the sidecar
        // alone: an element that survived an external edit keeps the id its connections are
        // already holding, so a `git pull` does not silently reset every selection.
        _identities[path] = WardleyIdentities.Reconcile(WardleyParser.Parse(GetOrLoad(path)), previous);

        Changed?.Invoke(this, new WardleyDocumentChangedEventArgs(path));
    }

    private static WardleyDocument Load(string path)
    {
        string text;
        try
        {
            // A body that does not exist yet is an empty document, not an error: the `.adp`
            // file may have been created a moment ago, and a map that cannot open at all is a
            // worse answer than an empty one (Requirement 2.4).
            text = File.Exists(path) ? SharedDocumentReader.ReadAllText(path) : "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not read {Path}; opening it as empty", path);
            text = "";
        }

        return WardleyDocument.Parse(text);
    }

    /// <summary>
    /// Writes to a temporary name in the same folder and moves it into place, so a reader
    /// either sees the previous file or the new one and never a half-written map
    /// (Requirement 3.6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The publish is <see cref="AdpFileWriter.Save" />'s, so the scratch file uses the
    /// <c>~adp-</c> pattern <c>HierarchyModel</c> already ignores and never surfaces in the
    /// explorer. This method hand-rolled that move until file-io-centralization task 5.2,
    /// because the writer could only create and a save is an overwrite; <c>Save</c> is that
    /// missing half, and the borrowed scratch-name constant is no longer the only thing shared.
    /// </para>
    /// <para>
    /// A failed write keeps the edit in memory rather than discarding it. Losing an edit
    /// because the disk refused would be worse than a save the user can retry once the file is
    /// writable again.
    /// </para>
    /// </remarks>
    private static bool WriteAtomically(string path, string text)
    {
        var directory = IoPath.GetDirectoryName(path);
        var folder = directory is { Length: > 0 } ? directory : ".";

        try
        {
            // The folder is this store's precondition rather than the writer's business: a
            // scratch file has nowhere to land if the directory is not there yet.
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // This borrowed AdpFileWriter's scratch-name constants while hand-rolling the move
            // around them. The constant was shared and the discipline was not; now both are.
            AdpFileWriter.Save(path, text);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The scratch file no longer needs removing here: AdpFileWriter.Save cleans up its
            // own temporary before letting the failure out, so this catch keeps only the part
            // that is this store's business - telling the user and keeping the edit in memory.
            _logger.Warning(exception, "Could not write {Path}; the change is kept in memory", path);
            return false;
        }
    }
}
