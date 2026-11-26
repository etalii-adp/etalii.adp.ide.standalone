using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Writes a map's text as it was: the undo of the first save that wrote what opening the map found (<see cref="MindmapOpening"/>),
/// and the redo of that undo.
/// </summary>
/// <param name="BodyPath">The map.</param>
/// <param name="Text">The text written.</param>
/// <param name="Opened">What opening found, held again once <paramref name="Text"/> is written; null for none.</param>
public sealed record RestoreMindmapCommand(string BodyPath, string Text, MindmapOpened? Opened) : ICommand;

internal sealed class RestoreMindmapCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<RestoreMindmapCommand>
{
    public async Task<CommandResult> ExecuteAsync(RestoreMindmapCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        MindmapDocument document;
        try
        {
            document = await Documents.GetOrLoadAsync(command.BodyPath, cancellationToken);
        }
        catch (MindmapFormatException exception)
        {
            return CommandResult.Failure(exception.Message);
        }

        // The text the file holds: what opening found is not in it until a save writes it.
        var opened = document.Opened;
        var current = opened?.File ?? document.ToText();
        var saved = await Documents.RestoreAsync(command.BodyPath, command.Text, command.Opened, cancellationToken);
        return CommandResult.FromSave(saved, command with { Text = current, Opened = opened });
    }
}
