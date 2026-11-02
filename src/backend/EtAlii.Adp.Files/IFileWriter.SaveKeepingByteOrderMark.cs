using System.Text;

namespace EtAlii.Adp.Files;

public static class FileWriterSaveKeepingByteOrderMarkExtension
{
    private static readonly byte[] Utf8Mark = [0xEF, 0xBB, 0xBF];

    extension(IFileWriter writer)
    {
        /// <summary>
        /// Publishes <paramref name="content"/> over the file at <paramref name="path"/> as UTF-8, with the byte-order mark
        /// that file starts with, and without one where it has none or does not exist yet.
        /// </summary>
        /// <remarks>
        /// For a document ADP edits rather than authors: a file somebody else wrote comes back from a save or an undo with
        /// the bytes it had, the mark included, which the string overload alone would drop (spec 071, T012).
        /// </remarks>
        /// <exception cref="IOException">The write or the move failed.</exception>
        /// <exception cref="UnauthorizedAccessException">The write or the move was refused.</exception>
        public Task SaveKeepingByteOrderMarkAsync(string path, string content, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(content);
            return StartsWithMark(path) && !content.StartsWith('\uFEFF')
                ? writer.SaveAsync(path, [.. Utf8Mark, .. Encoding.UTF8.GetBytes(content)], cancellationToken)
                : writer.SaveAsync(path, content, cancellationToken);
        }
    }

    /// <summary>Whether the file at <paramref name="path"/> starts with UTF-8's byte-order mark; a file that is missing or cannot be read does not.</summary>
    private static bool StartsWithMark(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> start = stackalloc byte[3];
            return stream.ReadAtLeast(start, 3, throwOnEndOfStream: false) == 3 && start.SequenceEqual(Utf8Mark);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
