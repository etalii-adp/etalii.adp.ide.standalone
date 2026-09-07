using System.Collections.Concurrent;
using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql;

/// <inheritdoc cref="ISparqlDocumentStore" />
public sealed class SparqlDocumentStore : ISparqlDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<SparqlDocumentStore>();

    private readonly ConcurrentDictionary<string, SparqlDocumentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event EventHandler<SparqlDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public SparqlDocumentEntry GetOrLoad(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _entries.GetOrAdd(path, Load);
    }

    /// <inheritdoc />
    public void Forget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _entries.TryRemove(path, out _);
    }

    /// <inheritdoc />
    public void Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // No self-write guard, unlike every sibling store: this store never writes, so any
        // change on disk is by definition someone else's and is always taken.
        _entries.TryRemove(path, out _);
        var entry = GetOrLoad(path);
        Changed?.Invoke(this, new SparqlDocumentChangedEventArgs(path, entry));
    }

    private static SparqlDocumentEntry Load(string path)
    {
        string text;
        try
        {
            if (!File.Exists(path))
            {
                // This module never creates query files - queries are authored in text
                // editors - so a missing body is a state to name, not one to repair.
                return new SparqlDocumentEntry(
                    "",
                    SparqlQueryModel.Empty,
                    $"{IoPath.GetFileName(path)} does not exist. Queries are authored in a text editor; this diagram draws an existing .rq file.",
                    0);
            }

            // Shared with whoever is writing: File.ReadAllText opens at FileShare.Read, and
            // Windows sharing is mutual, so a save landing while this read is in flight fails
            // and the author is told their query "could not be written". This module has no
            // writer of its own (NoWriterSurfaceTests), but the editor the query is authored
            // in does, which is precisely the contention this reader exists to permit.
            text = Documents.SharedDocumentReader.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not read {Path}", path);
            return new SparqlDocumentEntry(
                "",
                SparqlQueryModel.Empty,
                $"{IoPath.GetFileName(path)} could not be read: {exception.Message}",
                0);
        }

        try
        {
            return new SparqlDocumentEntry(text, SparqlParser.Parse(text), "", 0);
        }
        catch (SparqlParseException exception)
        {
            // A file that does not parse is an ordinary state for a file somebody is editing:
            // it opens as unavailable naming file, line and reason (Requirement 1.3).
            _logger.Debug(exception, "{Path} does not parse at line {Line}", path, exception.Line);
            return new SparqlDocumentEntry(text, SparqlQueryModel.Empty, exception.Message, exception.Line);
        }
    }
}
