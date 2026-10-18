using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Files;

/// <summary>
/// The <see cref="IFileWriter"/> the host registers: the publish and the delete of
/// <see cref="AdpFileWriter"/>, each taking its destination's turn, a turn held only while a save
/// or a delete of that path holds or waits for it (code-structure FR-068).
/// </summary>
public sealed class FileWriter : IFileWriter
{
    private readonly PathTurns _turns = new();

    /// <summary>How many destinations have a turn held or waited for; zero when nothing is being written.</summary>
    internal int TurnsHeld => _turns.Count;

    /// <summary>
    /// Resolved at the call site, as <see cref="AdpFileWriter"/>'s is and for its reason: a writer
    /// created before the host configures Serilog would otherwise be mute for the process's life.
    /// </summary>
    private static ILogger Logger => Log.ForContext<FileWriter>();

    public Task SaveAsync(string path, string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SaveAsync(path, content, null, AdpFileWriter.ReplaceDestination, cancellationToken);
    }

    public Task SaveAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SaveAsync(path, null, content, AdpFileWriter.ReplaceDestination, cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var destination = IoPath.GetFullPath(path);
        using var turn = await _turns.TakeAsync(destination, () => WaitedToDelete(destination), cancellationToken);
        AdpFileWriter.DeleteTaken(destination);
    }

    /// <summary>
    /// The save with the replace handed in, for guards: a guard can stop a save inside its replace
    /// and see a second save of the same path wait for it.
    /// </summary>
    internal async Task SaveAsync(string path, string? text, byte[]? bytes, Action<string, string> replace, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var destination = IoPath.GetFullPath(path);
        using var turn = await _turns.TakeAsync(destination, () => WaitedToSave(destination), cancellationToken);
        AdpFileWriter.Publish(path, text, bytes, replace);
    }

    /// <summary>The save of the static <see cref="AdpFileWriter.Save(string, string)"/>, which waits on its thread.</summary>
    internal void SaveOnThisThread(string path, string? text, byte[]? bytes, Action<string, string> replace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var destination = IoPath.GetFullPath(path);
        using var turn = _turns.Take(destination, () => WaitedToSave(destination));
        AdpFileWriter.Publish(path, text, bytes, replace);
    }

    /// <summary>The delete of the static <see cref="AdpFileWriter.Delete"/>, which waits on its thread.</summary>
    internal void DeleteOnThisThread(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var destination = IoPath.GetFullPath(path);
        using var turn = _turns.Take(destination, () => WaitedToDelete(destination));
        AdpFileWriter.DeleteTaken(destination);
    }

    // A wait is logged, by path, because a second writer of one path is worth knowing about: a
    // silent wait would hide it where a logged one names it.
    private static void WaitedToSave(string destination) =>
        Logger.Warning("Waited for another save to {Path} before publishing it", destination);

    private static void WaitedToDelete(string destination) =>
        Logger.Warning("Waited for a save of {Path} before deleting it", destination);
}
