using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>The changes one edit makes to one other knowledge file: the other side of a two-way relation.</summary>
/// <param name="BodyPath">The other file.</param>
/// <param name="Changes">What to change in it, in order.</param>
internal sealed record KnowledgeOtherFile(string BodyPath, IReadOnlyList<ModelChange> Changes);

/// <summary>
/// One edit of a knowledge file: the changes a gesture came to, made to the file as one step.
/// Plain data, so the history can run it again to redo it.
/// </summary>
/// <param name="BodyPath">The knowledge file.</param>
/// <param name="Changes">What to change, in order. All of them are written, or none.</param>
/// <param name="Others">What the same step changes in other files. Every file is written, or none is.</param>
internal sealed record KnowledgeEditCommand(string BodyPath, IReadOnlyList<ModelChange> Changes, IReadOnlyList<KnowledgeOtherFile>? Others = null) : ICommand
{
    public IReadOnlyList<KnowledgeOtherFile> Others { get; } = Others ?? [];
}

/// <summary>One file of a step that wrote several: what it held before, and what the step left it holding.</summary>
internal sealed record KnowledgeFileState(string BodyPath, string Before, string After);

/// <summary>
/// Puts several knowledge files back as they were before one step that wrote them all: the inverse
/// of an edit that made both sides of a two-way relation.
/// </summary>
/// <param name="Files">Each file with its content from before the step and after it.</param>
/// <param name="Redo">The original command, so redoing the undo makes the same edit again.</param>
internal sealed record RestoreKnowledgeFilesCommand(IReadOnlyList<KnowledgeFileState> Files, ICommand Redo) : IDocumentBoundCommand
{
    public string BodyPath => Files[0].BodyPath;
}

/// <summary>
/// Where the module's commands tell the open tables that a file was written or restored, so they
/// read it again at once rather than when the watcher gets round to it.
/// </summary>
internal interface IKnowledgeDocumentStore : IReloadableDocumentStore
{
    /// <summary>Raised with the path of a file that was just written through ADP.</summary>
    event Action<string>? Reloaded;
}

internal sealed class KnowledgeDocuments : IKnowledgeDocumentStore
{
    public event Action<string>? Reloaded;

    public void Reload(string path) => Reloaded?.Invoke(path);
}

/// <summary>
/// Carries out a <see cref="KnowledgeEditCommand"/>: reads the file as it is now, makes the changes
/// through the FBL runtime with the binding the file's format selects, and saves the result with
/// the central writer. Its inverse is the file's whole content from before, so an undo gives the
/// bytes back exactly.
/// </summary>
/// <remarks>
/// <para>
/// <b>All or nothing.</b> The changes are made to a copy of the open body; the first one the
/// runtime refuses ends the edit with its reason and the file is not written. So a file never
/// holds half a step - a deleted property whose cells stayed, a view without its columns.
/// </para>
/// <para>
/// <b>And so for a step over several files.</b> Every file's new content is made before any file
/// is written, so a refusal in the second leaves the first as it was; and when a write itself
/// fails after another has landed, what landed is put back.
/// </para>
/// </remarks>
internal sealed class KnowledgeEditCommandHandler(IKnowledgeDocumentStore documents) : ICommandHandler<KnowledgeEditCommand>
{
    public Task<CommandResult> ExecuteAsync(KnowledgeEditCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        // Plan every file before writing any.
        List<PlannedFile> planned = [];
        foreach (var file in command.Others.Prepend(new KnowledgeOtherFile(command.BodyPath, command.Changes)))
        {
            var name = Path.GetFileName(file.BodyPath);
            var read = KnowledgeDocumentStore.Read(file.BodyPath);
            if (read.Body is not { } body)
            {
                return Task.FromResult(CommandResult.Failure($"{name} could not be changed. {read.Refusal}"));
            }

            if (body.ReadOnlyReason.Length > 0)
            {
                return Task.FromResult(CommandResult.Failure(file.BodyPath == command.BodyPath ? body.ReadOnlyReason : $"{name} cannot be changed: {body.ReadOnlyReason}"));
            }

            (byte[]? after, string refusal) = body.Change(file.Changes);
            if (after is null)
            {
                return Task.FromResult(CommandResult.Failure(refusal));
            }

            planned.Add(new PlannedFile(file.BodyPath, body.Bytes, after));
        }

        var changed = planned.Where(file => !file.After.AsSpan().SequenceEqual(file.Before)).ToList();
        if (changed.Count == 0)
        {
            // Nothing to write and nothing to undo.
            return Task.FromResult(CommandResult.Success());
        }

        List<PlannedFile> written = [];
        foreach (var file in changed)
        {
            try
            {
                AdpFileWriter.Save(file.Path, file.After);
                written.Add(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // What landed before this one is put back: a step is every file or none.
                foreach (var landed in written)
                {
                    PutBack(landed.Path, landed.Before);
                }

                return Task.FromResult(CommandResult.Failure($"{Path.GetFileName(file.Path)} could not be written: {exception.Message}"));
            }
        }

        foreach (var file in written)
        {
            documents.Reload(file.Path);
        }

        if (written.Count > 1 || written[0].Path != command.BodyPath)
        {
            return Task.FromResult(CommandResult.Success(new RestoreKnowledgeFilesCommand(
                [.. written.Select(file => new KnowledgeFileState(file.Path, Encoding.UTF8.GetString(file.Before), Encoding.UTF8.GetString(file.After)))],
                command)));
        }

        // The restore keeps the file as text. A byte-order mark is a character of that text, so it
        // comes back with the rest; what the restore is checked against is given only for a file
        // without one, since the shared reader that does the checking leaves a mark out.
        var only = written[0];
        var hasMark = only.After.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        return Task.FromResult(CommandResult.Success(new RestoreDocumentCommand<IKnowledgeDocumentStore>(
            command.BodyPath,
            Encoding.UTF8.GetString(only.Before),
            command,
            hasMark ? null : Encoding.UTF8.GetString(only.After))));
    }

    private static void PutBack(string path, byte[] content)
    {
        try
        {
            AdpFileWriter.Save(path, content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done here; the failure that is reported is the one that started this.
        }
    }

    /// <summary>One file of the step: where it is, what it holds, and what it will hold.</summary>
    private sealed record PlannedFile(string Path, byte[] Before, byte[] After);
}

/// <summary>Carries out a <see cref="RestoreKnowledgeFilesCommand"/>: every file back to what it held, or none.</summary>
internal sealed class RestoreKnowledgeFilesCommandHandler(IKnowledgeDocumentStore documents) : ICommandHandler<RestoreKnowledgeFilesCommand>
{
    public Task<CommandResult> ExecuteAsync(RestoreKnowledgeFilesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        // A file another program wrote since is not what the step left: restoring over it would take that change with it.
        foreach (var file in command.Files)
        {
            var read = KnowledgeDocumentStore.Read(file.BodyPath);
            if (read.Body is null || !string.Equals(Encoding.UTF8.GetString(read.Body.Bytes), file.After, StringComparison.Ordinal))
            {
                return Task.FromResult(CommandResult.Outdated(
                    $"{Path.GetFileName(file.BodyPath)} was changed by another program, so the edits made to it before that can no longer be undone.",
                    file.BodyPath));
            }
        }

        List<KnowledgeFileState> restored = [];
        foreach (var file in command.Files)
        {
            try
            {
                AdpFileWriter.Save(file.BodyPath, Encoding.UTF8.GetBytes(file.Before));
                restored.Add(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                foreach (var done in restored)
                {
                    try
                    {
                        AdpFileWriter.Save(done.BodyPath, Encoding.UTF8.GetBytes(done.After));
                    }
                    catch (Exception again) when (again is IOException or UnauthorizedAccessException)
                    {
                        // The failure that is reported is the one that started this.
                    }
                }

                return Task.FromResult(CommandResult.Failure($"Could not restore {Path.GetFileName(file.BodyPath)}: {exception.Message}"));
            }
        }

        foreach (var file in restored)
        {
            documents.Reload(file.BodyPath);
        }

        return Task.FromResult(CommandResult.Success(command.Redo));
    }
}
