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

    public static AdpFileWriteResult Create(string folder, string fileName, string firstLine)
    {
        var destination = IoPath.Combine(folder, fileName);
        var temporary = IoPath.Combine(folder, $"{TempPrefix}{Guid.NewGuid():N}{TempExtension}");

        try
        {
            // No BOM: the first line is meant to be readable as-is by anything that opens the
            // file, and a BOM would sit in front of it.
            File.WriteAllText(temporary, firstLine + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // The non-overwriting move is the create-new guarantee: if the name was taken in
            // the meantime, this throws rather than replacing what is there.
            File.Move(temporary, destination, overwrite: false);

            // Information: a file appeared in the user's project because of us, which is
            // exactly the kind of thing worth being able to point at afterwards.
            _logger.Information("Created {FilePath}", destination);
            return new AdpFileWriteResult.Created(destination);
        }
        catch (IOException) when (File.Exists(destination) || Directory.Exists(destination))
        {
            // Something claimed the name between the check and the move. The user is told and
            // can pick another, so this is a warning about a race, not a failure.
            _logger.Warning("Did not create {FilePath}: the name was taken while it was being written", destination);
            DeleteQuietly(temporary);
            return new AdpFileWriteResult.NameTaken();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.Error(ex, "Failed to create {FilePath}", destination);
            DeleteQuietly(temporary);
            return new AdpFileWriteResult.Failed(ex.Message);
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
