using System.Collections.Concurrent;
using EtAlii.Adp.Backend.Hierarchy;
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

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var document = GetOrLoad(path);
        _selfWrites[path] = 1;
        try
        {
            if (!WriteAtomically(path, document.ToText()))
            {
                return;
            }

            // The edit may have added or removed elements, so identities are re-reconciled
            // against what was already assigned - anything that survived keeps its id - and
            // only now written. A map opened and never edited reaches none of this
            // (Requirement 4.3).
            var reconciled = WardleyIdentities.Reconcile(WardleyParser.Parse(document), Identities(path));
            _identities[path] = reconciled;
            _sidecar.Write(path, reconciled);
        }
        finally
        {
            _selfWrites.TryRemove(path, out _);
        }

        Changed?.Invoke(this, new WardleyDocumentChangedEventArgs(path));
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
            text = File.Exists(path) ? File.ReadAllText(path) : "";
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
    /// The temporary uses <see cref="AdpFileWriter.TempPrefix"/>, which <c>HierarchyModel</c>
    /// already ignores - so ADP's own scratch file never surfaces in the explorer, and the
    /// watcher does not report it as a new entry. It cannot use <c>AdpFileWriter</c> itself:
    /// that creates files and refuses to overwrite, and a save is an overwrite.
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
        var temporary = IoPath.Combine(
            folder,
            $"{AdpFileWriter.TempPrefix}{Guid.NewGuid():N}{AdpFileWriter.TempExtension}");

        try
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not write {Path}; the change is kept in memory", path);

            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(cleanup, "Could not remove the temporary file {Temporary}", temporary);
            }

            return false;
        }
    }
}
