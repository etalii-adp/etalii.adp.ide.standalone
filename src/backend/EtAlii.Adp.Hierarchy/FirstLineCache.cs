using System.Collections.Concurrent;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The first line of each file a designer's envelope might be on (spec 029 R3): the bytes up to the first line break,
/// or <see cref="Limit"/> bytes, whichever comes first, kept until the file changes. Routing a file of a shared extension
/// reads no more of it than this, and a tree refresh reads nothing a second time.
/// </summary>
/// <remarks>
/// A file is taken to have changed when its length or its last write time has; a change that keeps both is not seen,
/// which is the price of not reading the file to find out.
/// </remarks>
public sealed class FirstLineCache
{
    /// <summary>The most of a file's first line that is read.</summary>
    public const int Limit = 1024;

    private readonly ConcurrentDictionary<string, (long Length, DateTime Written, byte[] Line)> _lines = new(StringComparer.Ordinal);

    /// <summary>The first line of <paramref name="path"/>, without its line break; empty for a file that is not there or cannot be read.</summary>
    public byte[] Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            _lines.TryRemove(path, out _);
            return [];
        }

        (long length, DateTime written) = (file.Length, file.LastWriteTimeUtc);
        if (_lines.TryGetValue(path, out var kept) && kept.Length == length && kept.Written == written)
        {
            return kept.Line;
        }

        var line = ReadLine(path);
        _lines[path] = (length, written, line);
        return line;
    }

    private static byte[] ReadLine(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Limit);
            var buffer = new byte[Limit];
            var read = stream.ReadAtLeast(buffer, Limit, throwOnEndOfStream: false);
            var end = Array.FindIndex(buffer, 0, read, value => value is (byte)'\n' or (byte)'\r');
            return buffer[..(end < 0 ? read : end)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
