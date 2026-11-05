using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// Carries out a <see cref="HierarchicalRequirementEditCommand"/>: checks that the body still holds the bytes the edit
/// was planned against, makes the edit through the FBL library, and saves the result with the central writer: the body,
/// and the registration too when the step changes a list it keeps. Its inverse is a
/// <see cref="RestoreRequirementFilesCommand"/> of the bytes of each file from before, so an undo gives them back exactly,
/// and refuses once another program has written one of them since.
/// </summary>
internal sealed class HierarchicalRequirementEditCommandHandler(IHierarchicalRequirementDocumentStore documents, IFileWriter writer) : ICommandHandler<HierarchicalRequirementEditCommand>
{
    public async Task<CommandResult> ExecuteAsync(HierarchicalRequirementEditCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var name = Path.GetFileName(command.BodyPath);
        byte[] before;
        try
        {
            before = SharedDocumentReader.ReadAllBytes(command.BodyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"{name} could not be read: {exception.Message}");
        }

        if (!before.AsSpan().SequenceEqual(command.Expected))
        {
            return CommandResult.Failure(HierarchicalRequirementGestures.Drifted);
        }

        if (TagOperations.Handles(command.Operation) || CategoryStateOperations.Handles(command.Operation) || FilterOperations.Handles(command.Operation) || ColumnOperations.Handles(command.Operation))
        {
            return await WriteAsync(command, Lists(command, before, name), cancellationToken);
        }

        (byte[]? after, string refusal) = command.Operation switch
        {
            _ when TextOperations.Handles(command.Operation) => TextOperations.Apply(before, name, command.Operation, command.TargetId, command.Params.GetValueOrDefault(TextOperations.AttributeOf(command.Operation), "")),
            _ when StructureOperations.Handles(command.Operation) => StructureOperations.Apply(before, name, command.Operation, command.TargetId, command.Params),
            _ when AssessmentOperations.Handles(command.Operation) => AssessmentOperations.Apply(before, name, command.Operation, command.TargetId, command.Params),
            _ => (null, $"'{command.Operation}' cannot be done here yet."),
        };
        if (after is null)
        {
            return CommandResult.Failure(refusal);
        }

        return await WriteAsync(command, ([new RequirementFileState(command.BodyPath, before, after)], ""), cancellationToken);
    }

    /// <summary>
    /// What an operation on a list the registration keeps writes, the tags, the categories, the states, the filters or the widths: the
    /// body when its entries change, and the registration when the list does, the body first. A registration the body has none of is created as the step's starting point, outside the step.
    /// </summary>
    private static (List<RequirementFileState> Files, string Refusal) Lists(HierarchicalRequirementEditCommand command, byte[] body, string name)
    {
        var registrationPath = command.RegistrationPath ?? Path.ChangeExtension(command.BodyPath, ".adp");
        byte[]? registration;
        try
        {
            registration = File.Exists(registrationPath) ? SharedDocumentReader.ReadAllBytes(registrationPath) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ([], $"{Path.GetFileName(registrationPath)} could not be read: {exception.Message}");
        }

        var registrationName = Path.GetFileName(registrationPath);
        var edit = command.Operation switch
        {
            _ when CategoryStateOperations.Handles(command.Operation) => CategoryStateOperations.Apply(body, name, registration, registrationName, command.Operation, command.TargetId, command.Params),
            _ when FilterOperations.Handles(command.Operation) => FilterOperations.Apply(body, name, registration, registrationName, command.Operation, command.TargetId, command.Params),
            _ when ColumnOperations.Handles(command.Operation) => ColumnOperations.Apply(body, name, registration, registrationName, command.Operation, command.TargetId, command.Params),
            _ => TagOperations.Apply(body, name, registration, registrationName, command.Operation, command.TargetId, command.Params),
        };
        if (edit.Refusal.Length > 0) return ([], edit.Refusal);
        List<RequirementFileState> files = [];
        if (edit.Body is { } after) files.Add(new RequirementFileState(command.BodyPath, body, after));
        if (edit.RegistrationAfter is { } kept) files.Add(new RequirementFileState(registrationPath, edit.RegistrationBefore!, kept));
        return (files, "");
    }

    /// <summary>
    /// Writes each file the step changes, every one or none; then has the pages read them, and returns the step's inverse,
    /// which puts back what each held before it.
    /// </summary>
    private async Task<CommandResult> WriteAsync(HierarchicalRequirementEditCommand command, (List<RequirementFileState> Files, string Refusal) step, CancellationToken cancellationToken)
    {
        if (step.Refusal.Length > 0)
        {
            return CommandResult.Failure(step.Refusal);
        }

        var files = step.Files.Where(file => !file.After.AsSpan().SequenceEqual(file.Before)).ToList();
        if (files.Count == 0)
        {
            // Nothing to write and nothing to undo.
            return CommandResult.Success();
        }

        List<RequirementFileState> written = [];
        foreach (var file in files)
        {
            documents.WillWrite(file.Path, file.After);
            try
            {
                await writer.SaveAsync(file.Path, file.After, cancellationToken);
                written.Add(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // What was written before this one is put back: a step is every file or none. A registration this step
                // created is put back as created, which is not part of the step (FBL 0.7 section 8.4).
                foreach (var done in written)
                {
                    documents.WillWrite(done.Path, done.Before);
                    try
                    {
                        await writer.SaveAsync(done.Path, done.Before, CancellationToken.None);
                    }
                    catch (Exception again) when (again is IOException or UnauthorizedAccessException)
                    {
                        // The failure that is reported is the one that started this.
                    }
                }

                return CommandResult.Failure($"{Path.GetFileName(file.Path)} could not be written: {exception.Message}");
            }
        }

        foreach (var file in written)
        {
            // Not with the request's token: the file is written, so the pages read it whatever becomes of the request.
            await documents.ReloadAsync(file.Path, CancellationToken.None);
        }

        return CommandResult.Success(new RestoreRequirementFilesCommand(written, command));
    }
}
