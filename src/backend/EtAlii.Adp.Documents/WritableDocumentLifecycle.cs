using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Documents.Wire.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Documents;

/// <summary>
/// A <see cref="DocumentLifecycle{TDocument}"/> for a store that also writes: everything the
/// read-only lifecycle does, plus a save and the bookkeeping that stops the store's own write
/// bouncing back through a reload (backend-centralization R2.3).
/// </summary>
/// <remarks>
/// Composed rather than inherited, and the store composes it in turn: the store keeps its own public
/// save, its own change event and any state of its own, and delegates the lifecycle to this.
/// </remarks>
/// <typeparam name="TDocument">What the store caches, as for <see cref="DocumentLifecycle{TDocument}"/>.</typeparam>
public sealed class WritableDocumentLifecycle<TDocument>
    where TDocument : class
{
    private static readonly ILogger _logger = Log.ForContext<WritableDocumentLifecycle<TDocument>>();

    private readonly DocumentLifecycle<TDocument> _lifecycle;
    private readonly Func<TDocument, string> _serialize;
    private readonly Action<string, string> _write;

    // The paths this lifecycle is writing, and what it last wrote to each, so its own save does not
    // come back through Reload as an external change - per path, as every writable store kept it
    // before.
    private readonly SelfWriteGuard _selfWrites = new();

    /// <param name="parse">As for <see cref="DocumentLifecycle{TDocument}"/>.</param>
    /// <param name="serialize">The text a document is saved as.</param>
    /// <param name="unavailable">As for <see cref="DocumentLifecycle{TDocument}"/>.</param>
    public WritableDocumentLifecycle(
        Func<string, string, TDocument> parse,
        Func<TDocument, string> serialize,
        Func<string, DocumentUnavailability, string, TDocument>? unavailable = null)
        : this(parse, serialize, unavailable, AdpFileWriter.Save)
    {
    }

    /// <summary>With the write itself supplied, so a test can stand inside it.</summary>
    internal WritableDocumentLifecycle(
        Func<string, string, TDocument> parse,
        Func<TDocument, string> serialize,
        Func<string, DocumentUnavailability, string, TDocument>? unavailable,
        Action<string, string> write)
        : this(new DocumentLifecycle<TDocument>(parse, unavailable), serialize, write)
    {
    }

    /// <summary>
    /// With the read and its retry supplied, so a module's own tests can refuse exactly as often as
    /// they choose and wait for nothing - deterministic rather than patient.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is for a converted store's EXISTING tests, not for a module to tune its retries.</b>
    /// c4 and causal-loop copied this lifecycle's retry before task 6 (<c>350b8f9e</c>), and their
    /// stores took the read and the retry through an internal constructor so their guards could
    /// script a refusal - one refused read, then a read count asserted - and so the 3000-reload race
    /// guard could drop the wait between attempts, which at 50 ms took it from about a second to
    /// fifteen. Task 6 says those tests are the proof of the conversion, unchanged; without this the
    /// converted store has nowhere to hand the read, and the tests would have to race a real handle
    /// instead, which proves less and waits more.
    /// </para>
    /// <para>
    /// <b>Internal, and visible only to the two modules whose tests need it</b> - see the
    /// <c>InternalsVisibleTo</c> entries in this project file. Production stores use the public
    /// constructor and the lifecycle's own numbers; a module that passed its own here would be
    /// choosing a retry R2.1 leaves no module room to choose.
    /// </para>
    /// </remarks>
    internal WritableDocumentLifecycle(
        Func<string, string, TDocument> parse,
        Func<TDocument, string> serialize,
        Func<string, DocumentUnavailability, string, TDocument>? unavailable,
        Func<string, string> read,
        int readAttempts,
        TimeSpan betweenReadAttempts)
        : this(new DocumentLifecycle<TDocument>(parse, unavailable, read, readAttempts, betweenReadAttempts), serialize, AdpFileWriter.Save)
    {
    }

    private WritableDocumentLifecycle(
        DocumentLifecycle<TDocument> lifecycle,
        Func<TDocument, string> serialize,
        Action<string, string> write)
    {
        ArgumentNullException.ThrowIfNull(serialize);
        ArgumentNullException.ThrowIfNull(write);
        _lifecycle = lifecycle;
        _serialize = serialize;
        _write = write;
    }

    /// <inheritdoc cref="DocumentLifecycle{TDocument}.GetOrLoad"/>
    public TDocument GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    /// <inheritdoc cref="DocumentLifecycle{TDocument}.Get"/>
    public TDocument? Get(string path) => _lifecycle.Get(path);

    /// <inheritdoc cref="DocumentLifecycle{TDocument}.Forget"/>
    public void Forget(string path)
    {
        _lifecycle.Forget(path);
        _selfWrites.Forget(path);
    }

    /// <summary>
    /// Re-reads a body changed on disk - unless the change is this lifecycle's own save in flight,
    /// which is not an external change and is ignored (R2.3).
    /// </summary>
    /// <returns>As for <see cref="DocumentLifecycle{TDocument}.Reload"/>.</returns>
    public bool Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return !_selfWrites.IsOwnWrite(path) && _lifecycle.Reload(path);
    }

    /// <inheritdoc cref="DocumentLifecycle{TDocument}.BodyDeleted"/>
    public bool BodyDeleted(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return !_selfWrites.IsWriting(path) && _lifecycle.BodyDeleted(path);
    }

    /// <summary>Writes <paramref name="document"/> to <paramref name="path"/> and caches it.</summary>
    /// <param name="document">
    /// <b>The document the caller edited, and the only one this can write.</b> There is no cache
    /// read anywhere on this path, and that is the whole of the design rather than a detail of it:
    /// until 2026-09-24, every one of the nine writable module stores wrote whatever its own cache
    /// held at the moment of saving. A reload landing between a command's edit and its save replaced
    /// that entry, so the save wrote the re-read file and REPORTED SUCCESS - and the command's
    /// inverse went onto the undo stack for a change the file never received. Fixed store by store
    /// at <c>6c4f90d6</c> and <c>37326a8c</c>; a shared save that read the cache would have put it
    /// back in all nine from one line, looking like consolidation rather than like a regression.
    /// </param>
    /// <returns>
    /// <see cref="DocumentSaveResult.Ok"/>, or a failure whose sentence a user can act on. A caller
    /// must surface a failure rather than drop it; task 3's guard fails the build on a dropped one.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The document is cached whether or not the write succeeded.</b> On success it is what the
    /// file now holds. On failure the edit stays in memory to be retried (R3.4) - including when a
    /// reload replaced the cached entry while the caller was editing, which would otherwise leave the
    /// edit reachable only through the caller's own reference.
    /// </para>
    /// <para>
    /// <b>A folder that is not there yet is created first</b>, because the writer's scratch file has
    /// nowhere to land without it. Seven of the nine writable stores each did this themselves before
    /// task 6 - shared behaviour by count, which R2.1 leaves a module no room to supply - so it is done
    /// once here. A folder that cannot be created is a failed write like any other.
    /// </para>
    /// </remarks>
    public DocumentSaveResult Save(string path, TDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        var text = _serialize(document);
        _selfWrites.Begin(path, text);
        try
        {
            CreateFolderOf(path);
            _write(path, text);

            // Cached while the self-write mark is still held, so a reload racing this save is either
            // ignored or arrives after the cache already holds what the file holds.
            _lifecycle.Install(path, document);
            return DocumentSaveResult.Ok;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not write {Path}; the change is kept in memory", path);
            _lifecycle.Install(path, document);
            return DocumentSaveResult.Failure($"{IoPath.GetFileName(path)} could not be written. The change is still here to try again.");
        }
        finally
        {
            _selfWrites.End(path);
        }
    }

    private static void CreateFolderOf(string path)
    {
        var directory = IoPath.GetDirectoryName(path);
        if (directory is { Length: > 0 } && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
