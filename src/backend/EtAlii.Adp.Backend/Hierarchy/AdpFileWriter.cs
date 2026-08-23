using System.Text;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Writes a new file with a single line of content, so that a reader either does not see the
/// file at all or sees it complete: the content is written to a temporary name first and the
/// move into place is what publishes it. Nothing here knows about diagrams - it is handed a
/// folder, a file name and a line.
/// </summary>
public static class AdpFileWriter
{
    /// <summary>
    /// The temporary name pattern. It lives in the destination folder because a move is only
    /// atomic within one volume, and <see cref="HierarchyModel"/> ignores this pattern so
    /// ADP's own scratch file never surfaces as an entry.
    /// </summary>
    public const string TempPrefix = "~adp-";

    public const string TempExtension = ".tmp";

    private static readonly ILogger _logger = Log.ForContext(typeof(AdpFileWriter));

    public static AdpFileWriteResult Create(string folder, string fileName, string firstLine) =>
        CreateAll(folder, [(fileName, firstLine + "\n")]);

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
            return new AdpFileWriteResult.Created(destinations[0]);
        }
        catch (IOException) when (destinations.Any(destination => !moved.Contains(destination) && (File.Exists(destination) || Directory.Exists(destination))))
        {
            // Something claimed a name between the check and the move. The user is told and
            // can pick another, so this is a warning about a race, not a failure.
            _logger.Warning("Did not create {FilePaths}: a name was taken while it was being written", destinations);
            RollBack(moved, temporaries);
            return new AdpFileWriteResult.NameTaken();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.Error(ex, "Failed to create {FilePaths}", destinations);
            RollBack(moved, temporaries);
            return new AdpFileWriteResult.Failed(ex.Message);
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
