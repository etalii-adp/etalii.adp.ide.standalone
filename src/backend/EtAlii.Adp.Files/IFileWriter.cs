namespace EtAlii.Adp.Files;

/// <summary>
/// Publishes and deletes the files ADP writes, one save or delete of a path at a time, so a reader
/// sees either the previous content or the new content complete (code-structure FR-068). Waiting
/// for another save of the same path blocks no thread. What a publish does is
/// <see cref="FileWriter.SaveAsync(string, string, CancellationToken)"/>'s, which says why.
/// </summary>
public interface IFileWriter
{
    /// <summary>Publishes <paramref name="content"/> over whatever is at <paramref name="path"/>, as UTF-8 without a byte-order mark.</summary>
    /// <exception cref="IOException">The write or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The write or the move was refused.</exception>
    Task SaveAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>Publishes exactly <paramref name="content"/> over whatever is at <paramref name="path"/>, a byte-order mark included.</summary>
    /// <exception cref="IOException">The write or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The write or the move was refused.</exception>
    Task SaveAsync(string path, byte[] content, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the file at <paramref name="path"/>, taking the turn a save of it takes, and records
    /// that it happened. Missing already is success.
    /// </summary>
    /// <exception cref="IOException">The delete failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The delete was refused.</exception>
    Task DeleteAsync(string path, CancellationToken cancellationToken);
}
