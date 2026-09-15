using System.Text;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Common.Wire.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Documents;

/// <summary>
/// Publishes file content so that a reader either does not see the change at all or sees it
/// complete: the content is written to a temporary name first and the move into place is what
/// publishes it. Nothing here knows about diagrams - it is handed a folder, a file name and
/// content, or a path and content.
/// </summary>
/// <remarks>
/// Two publishes, and the difference is the move's <c>overwrite</c> flag rather than the
/// algorithm. <see cref="Create" /> and <see cref="CreateAll" /> create something that was not
/// there, so a name already taken is a failure they report. <see cref="Save" /> replaces what
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

    private static readonly ILogger _logger = Log.ForContext(typeof(AdpFileWriter));

    /// <summary>
    /// One turn per destination, keyed by the normalised full path - see <see cref="Save(string, string)"/>.
    /// Ignoring case on Windows, where two spellings name one file and would otherwise take two
    /// turns and race exactly as before; case-sensitive elsewhere, where they are two files.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _destinationTurns =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static AdpFileWriteResult Create(string folder, string fileName, string firstLine) =>
        CreateAll(folder, [(fileName, firstLine + NewLine)]);

    /// <summary>
    /// Publishes <paramref name="content" /> over whatever is at <paramref name="path" />, or
    /// leaves that file untouched: the content goes to a scratch name in the same folder and
    /// the move into place replaces the old bytes in one step. A reader holding the file open
    /// sees the previous version until the move lands, never a half-written one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the overwriting half of the class, added for file-io-centralization task 5:
    /// four modules had hand-rolled exactly this - a <c>~adp-</c> scratch file and a
    /// <c>File.Move(overwrite: true)</c> - because <see cref="CreateAll" /> refuses to
    /// overwrite and a save is an overwrite. They were copies of this algorithm differing in
    /// one boolean, which is why all four convert here rather than being justified separately.
    /// </para>
    /// <para>
    /// <b>It imposes no error policy, deliberately.</b> A failed publish throws, and each
    /// caller keeps the policy it already had - the mindmap store lets it propagate, the
    /// Wardley store and the two sidecars catch it and keep the edit in memory. Centralizing
    /// the algorithm while quietly centralizing the error handling would have changed three
    /// modules' behaviour under the same commit that claimed to change none.
    /// </para>
    /// <para>
    /// The scratch file is removed if the write or the move fails, so a failure leaves the
    /// folder as it found it. The original exception is what surfaces; a failure to clean up
    /// never replaces it.
    /// </para>
    /// <para>
    /// <b>Concurrent saves to one destination take turns, and a save that waited says so.</b>
    /// Two threads publishing one path failed 1009 of 3000 saves with <c>IOException
    /// 0x80070497</c> (1175) <c>"Unable to remove the file to be replaced."</c> - the exception
    /// behind a flake in <c>DiagramElementActionFlowTests</c> - and, worse, produced
    /// <c>FileNotFoundException</c> and 1177, so a reader could briefly find the document missing
    /// or under a backup name. Measured on 2026-09-15; readers in any sharing mode never produced
    /// it. The turn is held across the whole save, so the destination always holds exactly one
    /// writer's complete content. The wait is logged, by path, because in the failing test the two
    /// saves were awaited one after the other: a second writer exists, and a silent lock would
    /// hide it where a logged wait names it.
    /// </para>
    /// <para>
    /// <b>This is not an error policy</b> in the sense above: a save still fails exactly as it did
    /// for any other reason, and each caller still decides what a failure means. A sharing
    /// violation (<c>0x80070020</c>) from a reader sharing only <c>Read</c> is not retried and
    /// still fails at once - that is ADP's own file-access bug class, and a runtime retry would
    /// be the wrong layer to meet it.
    /// </para>
    /// <para>
    /// <b>Scope, stated so it is not assumed.</b> In process only: two ADP processes publishing
    /// one file are not protected, and nothing measured requires it. <see cref="CreateAll"/> is
    /// not covered - it creates new files and refuses to overwrite, so it does not race this way.
    /// One turn object is kept per distinct destination saved in the process's lifetime, which is
    /// bounded by the documents a session touches.
    /// </para>
    /// </remarks>
    /// <exception cref="IOException">The write or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The write or the move was refused.</exception>
    public static void Save(string path, string content) =>
        Save(path, content, ReplaceDestination);

    /// <summary>
    /// <see cref="Save(string, string)"/> with the replace handed in. <b>The seam lets a guard stop
    /// a save inside its replace</b>, which is what makes the turn-taking deterministic to test:
    /// with the first save held there, a second save to the same destination either waits and
    /// says so or goes straight through, and no timing decides which. It also drives a failure
    /// with a chosen HResult, so the failure record can be asserted without needing the OS to
    /// produce one on request. The application runs the same code with the real replace.
    /// </summary>
    internal static void Save(string path, string content, Action<string, string> replace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var destination = IoPath.GetFullPath(path);
        var turn = _destinationTurns.GetOrAdd(destination, static _ => new object());
        var taken = false;
        try
        {
            // Try first, without waiting, so a wait is logged only when one really happened - and
            // only for another save to THIS destination, which is why the turns are per path and
            // not striped: a stripe would report contention between files nobody wrote twice.
            Monitor.TryEnter(turn, ref taken);
            if (!taken)
            {
                _logger.Warning("Waited for another save to {Path} before publishing it", destination);
                Monitor.Enter(turn, ref taken);
            }

            // NOTHING INSIDE THIS TURN MAY CALL BACK OUT. The turn is held across the scratch
            // write, the replace or move, and the cleanup - so the destination holds exactly one
            // writer's complete content by construction - and nothing below can re-enter Save or
            // wait on something that might. Adding a callback, an event or a caller-supplied
            // action here would make a deadlock possible; the replace handed in is a test seam
            // and must stay a file operation.
            Publish(path, content, replace);
        }
        finally
        {
            if (taken)
            {
                Monitor.Exit(turn);
            }
        }
    }

    private static void Publish(string path, string content, Action<string, string> replace)
    {
        var directory = IoPath.GetDirectoryName(path);
        var folder = directory is { Length: > 0 } ? directory : ".";
        var temporary = IoPath.Combine(folder, $"{TempPrefix}{Guid.NewGuid():N}{TempExtension}");

        try
        {
            // The same no-BOM encoding CreateAll writes: the callers this replaced either said
            // so explicitly or took File.WriteAllText's default, which is the same thing.
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

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
            _logger.Warning(
                "Could not publish {Path}: {ExceptionType} {HResult} {Message}",
                path,
                exception.GetType().Name,
                $"0x{exception.HResult:X8}",
                exception.Message);
            DeleteQuietly(temporary);
            throw;
        }

        _logger.Debug("Published {Path}", path);
    }

    private static void ReplaceDestination(string temporary, string path) =>
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
            _logger.Information("Created {FilePaths}", destinations);
            return new AdpFileCreated(destinations[0]);
        }
        catch (IOException) when (destinations.Any(destination => !moved.Contains(destination) && (File.Exists(destination) || Directory.Exists(destination))))
        {
            // Something claimed a name between the check and the move. The user is told and
            // can pick another, so this is a warning about a race, not a failure.
            _logger.Warning("Did not create {FilePaths}: a name was taken while it was being written", destinations);
            RollBack(moved, temporaries);
            return new AdpFileNameTaken();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.Error(ex, "Failed to create {FilePaths}", destinations);
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
            _logger.Debug(ex, "Could not remove the scratch file {Path}", path);
        }
    }
}
