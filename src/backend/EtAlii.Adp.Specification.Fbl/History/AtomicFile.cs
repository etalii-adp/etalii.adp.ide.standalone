namespace EtAlii.Adp.Specification.Fbl.History;

/// <summary>The atomic write FBL §6.6 asks for: a temporary file in the destination's folder, then a move over it.</summary>
public static class AtomicFile
{
    public static async Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full) ?? throw new ArgumentException("The path has no folder.", nameof(path));
        var temporary = Path.Combine(folder, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            if (File.Exists(full) && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(full));
            }
            File.Move(temporary, full, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }
}
