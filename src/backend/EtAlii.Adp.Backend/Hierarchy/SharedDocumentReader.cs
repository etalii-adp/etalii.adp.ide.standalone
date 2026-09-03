namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Opens a user-editable file for reading without contending with whoever may be writing it.
/// File.ReadAllText and friends open with FileShare.Read, and Windows sharing is mutual: such
/// a read is refused while a save's write handle is open, and - the direction that bites - a
/// save landing while such a read is in flight fails with a sharing violation the user sees
/// as "could not be written". FileShare.ReadWrite lets the write win; FileShare.Delete lets a
/// temp-then-move publish (<see cref="AdpFileWriter"/>, the write-side half of this
/// discipline) replace the file mid-read. The price is that a read overlapping an in-place
/// write can be torn; every caller already treats unparseable content as a recoverable state,
/// and the write's own change event schedules the read that corrects it.
/// </summary>
public static class SharedDocumentReader
{
    public static string ReadAllText(string path)
    {
        using var reader = OpenText(path);
        return reader.ReadToEnd();
    }

    public static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = OpenText(path, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>A line-by-line reader over the same sharing, for header scans that stop early.</summary>
    public static StreamReader OpenText(string path, FileOptions options = FileOptions.SequentialScan) =>
        new(new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096, options));
}
