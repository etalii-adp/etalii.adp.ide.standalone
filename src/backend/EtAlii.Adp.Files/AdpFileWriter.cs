using System.Text;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Files;

/// <summary>
/// Publishes file content so that a reader either does not see the change at all or sees it
/// complete: the content is written to a temporary name first and the move into place is what
/// publishes it. Nothing here knows about diagrams - it is handed a folder, a file name and
/// content, or a path and content.
/// </summary>
/// <remarks>
/// Two publishes, and the difference is the move's <c>overwrite</c> flag rather than the
/// algorithm. <see cref="Create" /> and <see cref="CreateAll" /> create something that was not
/// there, so a name already taken is a failure they report. <see cref="FileWriter.SaveAsync(string, string, CancellationToken)" /> replaces what
/// is there, which is what a document save is, so the same taken name is the expected case.
/// </remarks>
public static class AdpFileWriter
{
    /// <summary>
    /// The temporary name pattern. It lives in the destination folder because a move is only
    /// atomic within one volume, and <see href="HierarchyModel"/> ignores this pattern so
    /// ADP's own scratch file never surfaces as an entry.
    /// </summary>
    public const string TempPrefix = "~adp-";

    public const string TempExtension = ".tmp";

    /// <summary>The marker <c>File.Replace</c> puts between a destination's name and the hex of its backup.</summary>
    private const string ReplaceBackupMarker = "~RF";

    /// <summary>The extension of a <c>File.Replace</c> backup.</summary>
    private const string ReplaceBackupExtension = ".TMP";

    /// <summary>
    /// Whether a file name belongs to a write in flight rather than to anything a user made: one of
    /// ADP's own scratch files (<see cref="IsScratch"/>) or the backup <c>File.Replace</c> moves a
    /// destination aside to for the instant of a swap (<see cref="IsReplaceBackup"/>). The one test
    /// for a transient name, so the hierarchy, the problems and the save diagnostics agree on it
    /// (code-structure FR-050).
    /// </summary>
    /// <param name="name">A file name or a path; only its last segment is read.</param>
    public static bool IsTransient(string name) => IsScratch(name) || IsReplaceBackup(name);

    /// <summary>Whether a file name is one of ADP's own scratch files: <c>~adp-&lt;guid&gt;.tmp</c>.</summary>
    /// <param name="name">A file name or a path; only its last segment is read.</param>
    public static bool IsScratch(string name)
    {
        var file = IoPath.GetFileName(name);
        return file.StartsWith(TempPrefix, StringComparison.Ordinal)
            && file.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a file name is a <c>File.Replace</c> backup - <c>&lt;name&gt;~RF&lt;hex&gt;.TMP</c> -
    /// and, when <paramref name="of"/> is given, the backup of that destination.
    /// </summary>
    /// <param name="name">A file name or a path; only its last segment is read.</param>
    /// <param name="of">The destination's file name, or null for the backup of any.</param>
    public static bool IsReplaceBackup(string name, string? of = null)
    {
        var file = IoPath.GetFileName(name);
        if (!file.EndsWith(ReplaceBackupExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var marker = file.LastIndexOf(ReplaceBackupMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return false;
        }

        if (of is not null && !file.AsSpan(0, marker).Equals(IoPath.GetFileName(of), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hex = file.AsSpan(marker + ReplaceBackupMarker.Length, file.Length - marker - ReplaceBackupMarker.Length - ReplaceBackupExtension.Length);
        if (hex.IsEmpty)
        {
            return false;
        }

        foreach (var character in hex)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The terminator every NEW line ADP writes into a repository file gets: CRLF, the house
    /// style the root <c>.gitattributes</c> and <c>src/.editorconfig</c> agree on for the
    /// working tree.
    /// </summary>
    /// <remarks>
    /// One constant rather than a literal per call site, because each site choosing for itself
    /// is exactly how this went wrong once: file creation wrote LF while the layout writer
    /// wrote CRLF, and a registration created and then repositioned in the running app ended
    /// up with mixed endings inside one file. CLAUDE.md's line-endings section calls a tool
    /// that writes LF against the house style a bug to fix at the tool - this constant is that
    /// fix. It governs new content only: a writer REWRITING an existing line keeps that line's
    /// own terminator (see <c>RegistrationLayout</c>'s per-line preservation), which is what
    /// keeps a hand-authored file byte-stable.
    /// </remarks>
    public const string NewLine = "\r\n";

    /// <summary>
    /// Resolved AT THE CALL SITE rather than cached in a static field, which is this repository's
    /// usual shape and is unsafe for THIS class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured:</b> Serilog's unset <c>Log.Logger</c> is a <c>SilentLogger</c>, and a
    /// <c>private static readonly ILogger</c> evaluated at that moment IS that silent logger — for
    /// the life of the process. Configuring <c>Log.Logger</c> afterwards changes nothing for it,
    /// while a logger resolved after configuration logs normally: same process, same sink, one line
    /// apart. <b>Type-initialisation order is FIRST-USE order</b>, so two classes in one assembly
    /// can differ with nothing visible to tell them apart.
    /// </para>
    /// <para>
    /// <b>It happened.</b> The fifth field occurrence of <c>0x80070497</c> produced NO record at
    /// all — not "no process was holding it", which is an answer, but silence. The failure's own
    /// stack placed execution inside this class's catch while its warning appeared nowhere in the
    /// run. An instrument that is wired in some runs and not others is worse than a broken one,
    /// because it works often enough to be trusted.
    /// </para>
    /// <para>
    /// <b>What this does NOT fix:</b> 92 production classes hold a logger this way and 90 still do.
    /// Any of them whose type initialiser runs before the host configures Serilog is mute for that
    /// process's life, and the only symptom is the absence of lines nobody is looking for. The
    /// tree-wide question is <b>deferred by the user, not declined</b> — the pattern remains house
    /// style, and no session should convert files on its own initiative.
    /// </para>
    /// </remarks>
    private static ILogger Logger => Log.ForContext(typeof(AdpFileWriter));

    public static AdpFileWriteResult Create(string folder, string fileName, string firstLine) =>
        CreateAll(folder, [(fileName, firstLine + NewLine)]);

    /// <summary>
    /// Deletes <paramref name="destination"/>, a full path, and records that it happened, with the
    /// turn of that path already taken. Missing already is success.
    /// </summary>
    internal static void DeleteTaken(string destination)
    {
        if (!File.Exists(destination))
        {
            return;
        }

        File.Delete(destination);

        // Information rather than Debug: this is the line a future failed publish is read
        // against, and a record only kept at Debug is a record the gate log does not have.
        Logger.Information("Deleted {Path} from pid {ProcessId}", destination, Environment.ProcessId);
    }

    /// <summary>
    /// Publishes the content over <paramref name="path"/>, with the turn of that path already taken.
    /// NOTHING INSIDE THE TURN MAY CALL BACK OUT: it is held across the scratch write, the replace
    /// or move, and the cleanup - so the destination holds exactly one writer's complete content by
    /// construction - and nothing here can re-enter a save or wait on something that might. The
    /// replace handed in is a test seam and must stay a file operation.
    /// </summary>
    internal static void Publish(string path, string? text, byte[]? bytes, Action<string, string> replace)
    {
        var directory = IoPath.GetDirectoryName(path);
        var folder = directory is { Length: > 0 } ? directory : ".";
        var temporary = IoPath.Combine(folder, $"{TempPrefix}{Guid.NewGuid():N}{TempExtension}");

        // Which file the destination was just before the replace - so a failure can say whether it
        // is still that file, gone, or recreated. Not default: that would claim "present, file 0".
        var before = new DestinationIdentity(
            DestinationIdentity.State.Unknown, 0, 0, "the publish failed before the replace was reached");

        try
        {
            if (bytes is not null)
            {
                // Exactly the bytes handed in, byte-order mark and all: this caller has already
                // decided what the file's encoding is.
                File.WriteAllBytes(temporary, bytes);
            }
            else
            {
                // The same no-BOM encoding CreateAll writes: the callers this replaced either said
                // so explicitly or took File.WriteAllText's default, which is the same thing.
                File.WriteAllText(temporary, text!, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            // Replacing the destination is the whole difference from CreateAll: here the
            // destination existing is the expected case rather than the failure.
            //
            // File.Replace, NOT File.Move(overwrite: true). The two look interchangeable and are
            // not: a move-with-overwrite is denied while ANY handle is open on the destination,
            // including one opened FileShare.ReadWrite | Delete - which is what
            // SharedDocumentReader opens, and precisely the case this read/write pair exists to
            // permit. Measured, because the failure modes are indistinguishable from outside:
            //
            //     holder shares         WriteAllText   Move(overwrite)   Replace
            //     ReadWrite | Delete    OK             DENIED            OK
            //     Read                  denied         DENIED            denied
            //
            // A save landing while a reader held the document therefore failed here and nowhere
            // else, and rarely enough - about one concurrent run in twenty - to read as
            // flakiness rather than as a defect. A reader sharing only Read still denies the
            // replace and must: that reader has asked for the file not to change under it.
            // AdpFileWriter.SharingContract.Tests pins both directions.
            //
            // READ ON EVERY SAVE, because "before" must be taken before the outcome is known: one
            // attributes-only open that can neither block nor be blocked, and one metadata call -
            // microseconds, not nothing. See DestinationIdentity for why it is the file index and
            // never the creation time.
            before = DestinationIdentity.Of(path);
            if (File.Exists(path))
            {
                replace(temporary, path);
            }
            else
            {
                // Replace requires an existing destination. A first publish has none, and a
                // plain move is right there because there is nothing to replace.
                File.Move(temporary, path);
            }
        }
        catch (Exception exception)
        {
            // RECORD THE CODE, NOT ONLY THE MESSAGE. The failure behind a flake in
            // DiagramElementActionFlowTests reached the logs five times as a type and a message
            // and never once with its HResult, and for a day its code was unknown - until two
            // concurrent writers reproduced it as 0x80070497. Any failure left after the turns
            // above says what it is. The exception still propagates unchanged; logging it imposes
            // no error policy on any caller.
            // ONE RECORD, WITH WHO IN IT. The code says what happened and never who did it, and
            // the suspects differ: 0x80070497 comes from a second replacer or from a delete of the
            // destination, 0x80070020 from a reader sharing only Read. Asking who holds the file
            // turns the next red gate into a name instead of another day of inference. It cannot
            // name an actor that has already released - see FileHolders - so "no process was
            // holding it when asked" is an answer rather than the absence of one.
            //
            // THE DESTINATION IS READ FIRST, AT THE MOMENT OF FAILURE. It used to be the last
            // argument, and C# evaluates arguments in order, so it ran AFTER FileHolders.Describe -
            // which waits up to its budget and answered in 728, 1,132 and 1,166 ms in the three
            // field records that time it. The folder was therefore described most of a second after
            // the replace failed, and a competing ADP publish - one of the two things 0x80070497 can
            // be, a delete of the destination being the other - creates its ~adp-*.tmp, replaces and
            // deletes it well inside that. It left nothing, and the record read "only our own" for
            // exactly the case the sibling scan exists to catch. Reading the folder
            // before the holder query makes "in flight" mean what the record always claimed: in
            // flight when this publish failed. The record's wording and field order are unchanged,
            // so records written before and after this line stay comparable - but a pre-fix
            // "only our own" is the weaker claim, and the evidence README says which records are.
            //
            // THE IDENTITY IS READ WITH THE FOLDER, before the holder query, for the same reason:
            // "recreated" means recreated by the time this publish failed, not a second later. It is
            // the LAST field, so every field before it keeps its position and older records stay
            // comparable; a record without it predates this line.
            var destination = DestinationState.Describe(path, temporary);
            var identity = DestinationIdentity.Describe(before, path);
            var holders = FileHolders.Describe(path);
            Logger.Warning(exception, "Could not publish {Path}: {ExceptionType} {HResult} {Message}; this process is pid {ProcessId}, holders: {Holders}, destination: {Destination}, identity: {Identity}",
                path,
                exception.GetType().Name,
                $"0x{exception.HResult:X8}",
                exception.Message,
                Environment.ProcessId,
                holders,
                destination,
                identity);
            DeleteQuietly(temporary);
            throw;
        }

        Logger.Debug("Published {Path}", path);
    }

    internal static void ReplaceDestination(string temporary, string path) =>
        File.Replace(temporary, path, destinationBackupFileName: null, ignoreMetadataErrors: true);

    /// <summary>
    /// Creates every file in <paramref name="files"/> or none of them: all are written to
    /// temporary names first, then moved into place one by one, and a move that fails undoes
    /// the moves before it. A reader can therefore never see a diagram with its registration
    /// file but not its body, or the other way round (mindmap-diagram Requirement 1.6).
    /// </summary>
    /// <remarks>
    /// The rollback is best-effort in one respect: the process dying between two moves leaves
    /// the first in place. The temp-then-move discipline makes that window two filesystem
    /// calls wide, which is as narrow as it gets without a journal.
    /// </remarks>
    public static AdpFileWriteResult CreateAll(string folder, IReadOnlyList<(string FileName, string Content)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentOutOfRangeException.ThrowIfZero(files.Count);

        var destinations = files.Select(file => IoPath.Combine(folder, file.FileName)).ToArray();
        var temporaries = files.Select(_ => IoPath.Combine(folder, $"{TempPrefix}{Guid.NewGuid():N}{TempExtension}")).ToArray();
        var moved = new List<string>(files.Count);

        try
        {
            // No BOM: the first line is meant to be readable as-is by anything that opens the
            // file, and a BOM would sit in front of it.
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            for (var i = 0; i < files.Count; i++)
            {
                File.WriteAllText(temporaries[i], files[i].Content, encoding);
            }

            // The non-overwriting move is the create-new guarantee: if a name was taken in
            // the meantime, this throws rather than replacing what is there.
            for (var i = 0; i < files.Count; i++)
            {
                File.Move(temporaries[i], destinations[i], overwrite: false);
                moved.Add(destinations[i]);
            }

            // Information: files appeared in the user's project because of us, which is
            // exactly the kind of thing worth being able to point at afterwards.
            Logger.Information("Created {FilePaths}", destinations);
            return new AdpFileCreated(destinations[0]);
        }
        catch (IOException) when (destinations.Any(destination => !moved.Contains(destination) && (File.Exists(destination) || Directory.Exists(destination))))
        {
            // Something claimed a name between the check and the move. The user is told and
            // can pick another, so this is a warning about a race, not a failure.
            Logger.Warning("Did not create {FilePaths}: a name was taken while it was being written", destinations);
            RollBack(moved, temporaries);
            return new AdpFileNameTaken();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Logger.Error(ex, "Failed to create {FilePaths}", destinations);
            RollBack(moved, temporaries);
            return new AdpFileWriteFailed(ex.Message);
        }
    }

    /// <summary>Removes what did get moved into place, and every scratch file, after a failure part-way.</summary>
    private static void RollBack(IEnumerable<string> moved, IEnumerable<string> temporaries)
    {
        foreach (var path in moved)
        {
            DeleteQuietly(path);
        }

        foreach (var path in temporaries)
        {
            DeleteQuietly(path);
        }
    }

    /// <summary>
    /// Cleans up the temporary file without letting its own failure replace the error that
    /// brought us here. A leftover is harmless anyway: the model never shows this pattern.
    /// </summary>
    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Deliberately not rethrown; Debug so a folder slowly filling with scratch files
            // still has an explanation somewhere.
            Logger.Debug(ex, "Could not remove the scratch file {Path}", path);
        }
    }
}
