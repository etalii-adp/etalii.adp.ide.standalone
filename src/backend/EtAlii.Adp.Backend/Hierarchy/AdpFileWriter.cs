using System.Text;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

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
    /// atomic within one volume, and <see cref="HierarchyModel"/> ignores this pattern so
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
    /// </remarks>
    /// <exception cref="IOException">The write or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The write or the move was refused.</exception>
    public static void Save(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var directory = IoPath.GetDirectoryName(path);
        var folder = directory is { Length: > 0 } ? directory : ".";
        var temporary = IoPath.Combine(folder, $"{TempPrefix}{Guid.NewGuid():N}{TempExtension}");

        try
        {
            // The same no-BOM encoding CreateAll writes: the callers this replaced either said
            // so explicitly or took File.WriteAllText's default, which is the same thing.
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // The overwriting move is the whole difference from CreateAll: here the
            // destination existing is the expected case rather than the failure.
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            DeleteQuietly(temporary);
            throw;
        }

        _logger.Debug("Published {Path}", path);
    }

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
