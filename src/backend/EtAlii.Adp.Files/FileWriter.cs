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

    /// <summary>
    /// Publishes <paramref name="content" /> over whatever is at <paramref name="path" />, or
    /// leaves that file untouched: the content goes to a scratch name in the same folder and
    /// the move into place replaces the old bytes in one step. A reader holding the file open
    /// sees the previous version until the move lands, never a half-written one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the overwriting half of <see cref="AdpFileWriter" />, added for file-io-centralization task 5:
    /// four modules had hand-rolled exactly this - a <c>~adp-</c> scratch file and a
    /// <c>File.Move(overwrite: true)</c> - because <see cref="AdpFileWriter.CreateAll" /> refuses to
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
    /// <para>
    /// <b>Concurrent saves to one destination take turns, and a save that waited says so.</b>
    /// Two threads publishing one path failed 1009 of 3000 saves with <c>IOException
    /// 0x80070497</c> (1175) <c>"Unable to remove the file to be replaced."</c> - the exception
    /// behind a flake in <c>DiagramElementActionFlowTests</c> - and, worse, produced
    /// <c>FileNotFoundException</c> and 1177, so a reader could briefly find the document missing
    /// or under a backup name. Measured on 2026-09-15; readers in any sharing mode never produced
    /// it. The turn is held across the whole save, so the destination always holds exactly one
    /// writer's complete content. The wait is logged, by path, because in the failing test the two
    /// saves were awaited one after the other: a second writer exists, and a silent lock would
    /// hide it where a logged wait names it.
    /// </para>
    /// <para>
    /// <b>This is not an error policy</b> in the sense above: a save still fails exactly as it did
    /// for any other reason, and each caller still decides what a failure means. A sharing
    /// violation (<c>0x80070020</c>) from a reader sharing only <c>Read</c> is not retried and
    /// still fails at once - that is ADP's own file-access bug class, and a runtime retry would
    /// be the wrong layer to meet it.
    /// </para>
    /// <para>
    /// <b>Scope, stated so it is not assumed.</b> In one writer only - the host registers one for the process - and in process only: two ADP processes publishing
    /// one file are not protected, and nothing measured requires it. <see cref="AdpFileWriter.CreateAll"/> is
    /// not covered - it creates new files and refuses to overwrite, so it does not race this way.
    /// A destination's turn is kept only while a save or a delete of it holds or waits for it
    /// (code-structure FR-068).
    /// </para>
    /// </remarks>
    /// <exception cref="IOException">The write or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The write or the move was refused.</exception>
    public Task SaveAsync(string path, string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SaveAsync(path, content, null, AdpFileWriter.ReplaceDestination, cancellationToken);
    }

    /// <summary>
    /// <see cref="SaveAsync(string, string, CancellationToken)"/> for content that is already
    /// bytes - a file whose exact encoding is the caller's to decide, a byte-order mark above all.
    /// </summary>
    /// <remarks>
    /// The string overload writes UTF-8 without a BOM, which is right for documents ADP AUTHORS
    /// and wrong for one it merely EDITS: a text buffer round-tripping somebody else's file has
    /// to put back the BOM it found. That is why this exists rather than the caller reaching for
    /// its own FileStream - it takes the same per-destination turn and the same temp-then-replace
    /// publish, so a reader never sees a half-written file and a delete cannot interleave.
    /// </remarks>
    /// <exception cref="IOException">The write or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The write or the move was refused.</exception>
    public Task SaveAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SaveAsync(path, null, content, AdpFileWriter.ReplaceDestination, cancellationToken);
    }

    /// <summary>
    /// Deletes a file ADP publishes, taking the SAME turn a save of that path takes, and records
    /// that it happened. Missing already is success: the end state is what was asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a delete needs the save's turn.</b> A delete interleaved with a publish of the same
    /// path breaks the publish, and the codes it produces are the ones that cost a day: measured,
    /// 400 replaces against a concurrent delete-and-recreate failed 305 times - 75 of them
    /// <c>0x80070497</c>, the gate flake's own code - plus <c>0x800700B7</c>, <c>0x80070002</c>,
    /// <c>0x80070020</c> and <c>0x80070005</c>. Sharing one turn: 400 of 400 succeeded, no failure
    /// of any code. The lock was serialising writers against writers only, and a deleter was never
    /// a writer to it.
    /// </para>
    /// <para>
    /// <b>The record is the other half.</b> Restart Manager can only name a process still holding
    /// the file, and a completed delete holds nothing - so a publish that lost to a delete finds
    /// nobody to name. A line per delete, with the process and the path, is what a later failure
    /// can be correlated against.
    /// </para>
    /// </remarks>
    /// <exception cref="IOException">The delete failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The delete was refused.</exception>
    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var destination = IoPath.GetFullPath(path);
        using var turn = await _turns.TakeAsync(destination, () => WaitedToDelete(destination), cancellationToken);
        AdpFileWriter.DeleteTaken(destination);
    }

    /// <summary>
    /// <see cref="SaveAsync(string, string, CancellationToken)"/> with the replace handed in.
    /// <b>The seam lets a guard stop a save inside its replace</b>, which is what makes the turn-taking deterministic to test:
    /// with the first save held there, a second save to the same destination either waits and
    /// says so or goes straight through, and no timing decides which. It also drives a failure
    /// with a chosen HResult, so the failure record can be asserted without needing the OS to
    /// produce one on request. The application runs the same code with the real replace.
    /// </summary>
    internal Task SaveAsync(string path, string content, Action<string, string> replace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SaveAsync(path, content, null, replace, cancellationToken);
    }

    private async Task SaveAsync(string path, string? text, byte[]? bytes, Action<string, string> replace, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var destination = IoPath.GetFullPath(path);
        using var turn = await _turns.TakeAsync(destination, () => WaitedToSave(destination), cancellationToken);
        AdpFileWriter.Publish(path, text, bytes, replace);
    }

    // A wait is logged, by path, because a second writer of one path is worth knowing about: a
    // silent wait would hide it where a logged one names it.
    private static void WaitedToSave(string destination) =>
        Logger.Warning("Waited for another save to {Path} before publishing it", destination);

    private static void WaitedToDelete(string destination) =>
        Logger.Warning("Waited for a save of {Path} before deleting it", destination);
}
