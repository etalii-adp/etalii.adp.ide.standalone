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

    public event EventHandler<WardleyDocumentChangedEventArgs>? Changed;

    public WardleyDocument GetOrLoad(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _documents.GetOrAdd(path, Load);
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var document = GetOrLoad(path);
        if (!WriteAtomically(path, document.ToText()))
        {
            return;
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
    }

    public void Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _documents.TryRemove(path, out _);
        _ = GetOrLoad(path);
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
