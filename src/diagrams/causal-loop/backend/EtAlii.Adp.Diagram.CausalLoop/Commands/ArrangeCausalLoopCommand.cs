using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// Runs the self-organizing layout and stores what it produced as authored positions
/// (causal-loop-diagram Requirement 6.8).
/// </summary>
/// <remarks>
/// <para>
/// <b>The body is never touched.</b> An arrangement is an opinion about where things should be
/// drawn, not a change to what the document says, so it goes into the registration's
/// <c>layout:</c> block through the same mechanism a drag uses. Everything above the block
/// survives byte for byte, and a <c>.cld</c> reviewed in a pull request shows no diff at all.
/// </para>
/// <para>
/// <b>One command, not one per variable.</b> Dispatching a
/// <see cref="SetRegistrationLayoutCommand"/> per box would make undoing a forty-variable
/// arrangement forty undos, which is not what "one undo away like every other edit" means. So
/// this is a single command whose inverse restores the registration's prior text — the same
/// shape <see cref="RestoreDocumentCommand{TStore}"/> uses for the body, and for the same
/// reason: a snapshot cannot be wrong about what it is putting back.
/// </para>
/// </remarks>
/// <param name="AdpPath">The registration whose <c>layout:</c> block receives the arrangement.</param>
/// <param name="BodyPath">The <c>.cld</c> the arrangement is computed from, and never written to.</param>
public sealed record ArrangeCausalLoopCommand(string AdpPath, string BodyPath) : ICommand;

/// <summary>
/// Puts a registration back byte for byte — the inverse of an arrangement, so one undo restores
/// the <c>layout:</c> block, the entries that were in it, and everything around it.
/// </summary>
/// <param name="AdpPath">The registration to restore.</param>
/// <param name="Text">Its complete text as captured before the arrangement.</param>
/// <param name="Redo">The original command, so redoing the undo arranges again.</param>
public sealed record RestoreCausalLoopRegistrationCommand(string AdpPath, string Text, ICommand Redo) : ICommand;

/// <inheritdoc cref="ArrangeCausalLoopCommand" />
public sealed class ArrangeCausalLoopCommandHandler(ICausalLoopDocumentStore documents)
    : ICommandHandler<ArrangeCausalLoopCommand>
{
    public Task<CommandResult> ExecuteAsync(
        ArrangeCausalLoopCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(command.AdpPath))
        {
            return Task.FromResult(CommandResult.Failure("The registration file is no longer there."));
        }

        var entry = documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This causal loop diagram could not be read, so there is nothing to arrange."));
        }

        // The refusal is the layout's own sentence, passed through rather than reworded: it
        // already names the size, and a command that rewrote it would make the same failure read
        // two different ways depending on where a user met it.
        var arrangement = SelfOrganizingLayout.Compute(entry.Model);
        if (!arrangement.IsArranged)
        {
            return Task.FromResult(CommandResult.Failure(arrangement.Refusal));
        }

        if (arrangement.Boxes.Count == 0)
        {
            return Task.FromResult(CommandResult.Failure("This diagram has no variables to arrange."));
        }

        string before;
        try
        {
            before = SharedDocumentReader.ReadAllText(command.AdpPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not read the registration: {exception.Message}"));
        }

        try
        {
            // In the document's own order, so the block is written the same way every time and
            // two runs of the same arrangement produce byte-identical registrations.
            foreach (var variable in entry.Model.Variables)
            {
                if (!arrangement.Boxes.TryGetValue(variable.Id, out var box))
                {
                    continue;
                }

                // Stored positions are centres - the render subtracts the half-extents again -
                // and they are keyed by the element id a drag reports, not by the bare variable.
                RegistrationLayout.SetPosition(
                    command.AdpPath,
                    $"variable:{variable.Id}",
                    new RegistrationPosition(box.CenterX, box.CenterY));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Put back whatever was there before giving up, so a half-written block is never
            // what the user is left looking at.
            Restore(command.AdpPath, before);
            return Task.FromResult(CommandResult.Failure($"Could not store the arrangement: {exception.Message}"));
        }

        return Task.FromResult(CommandResult.Success(
            new RestoreCausalLoopRegistrationCommand(command.AdpPath, before, command)));
    }

    private static void Restore(string adpPath, string text)
    {
        try
        {
            AdpFileWriter.Save(adpPath, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done here, and the caller is already reporting a failure.
        }
    }
}

/// <inheritdoc cref="RestoreCausalLoopRegistrationCommand" />
public sealed class RestoreCausalLoopRegistrationCommandHandler
    : ICommandHandler<RestoreCausalLoopRegistrationCommand>
{
    public Task<CommandResult> ExecuteAsync(
        RestoreCausalLoopRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            AdpFileWriter.Save(command.AdpPath, command.Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not restore the registration: {exception.Message}"));
        }

        return Task.FromResult(CommandResult.Success(command.Redo));
    }
}
