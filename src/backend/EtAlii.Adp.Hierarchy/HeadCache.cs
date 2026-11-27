using System.Collections.Concurrent;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The head of each file a designer type's marker might be in (FBL section 12.2, spec 093 R2): the file whole when it is no
/// longer than <see cref="Limit"/> bytes, else its first <see cref="Limit"/> bytes cut after the last line end among them, kept
/// until the file changes. Routing a file of a shared extension reads no more of it than this, and a tree refresh reads
/// nothing a second time.
/// </summary>
/// <remarks>
/// A file is taken to have changed when its length or its last write time has; a change that keeps both is not seen,
/// which is the price of not reading the file to find out. A first line longer than the limit is kept up to the limit.
/// </remarks>
public sealed class HeadCache
{
    /// <summary>The most of a file that is read: the part of a body FBL section 12.1 gives <c>suggest</c> to look at.</summary>
    public const int Limit = 64 * 1024;

    private readonly ConcurrentDictionary<string, (long Length, DateTime Written, byte[] Head)> _heads = new(StringComparer.Ordinal);

    /// <summary>The head of <paramref name="path"/>; empty for a file that is not there or cannot be read.</summary>
    public byte[] Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            _heads.TryRemove(path, out _);
            return [];
        }

        (long length, DateTime written) = (file.Length, file.LastWriteTimeUtc);
        if (_heads.TryGetValue(path, out var kept) && kept.Length == length && kept.Written == written)
        {
            return kept.Head;
        }

        var head = ReadHead(path);
        _heads[path] = (length, written, head);
        return head;
    }

    private static byte[] ReadHead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            var buffer = new byte[Limit + 1];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (read <= Limit)
            {
                return buffer[..read];
            }

            var end = Array.LastIndexOf(buffer, (byte)'\n', Limit - 1, Limit);
            return buffer[..(end < 0 ? Limit : end + 1)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
