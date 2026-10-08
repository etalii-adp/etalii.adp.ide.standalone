using System.Globalization;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Checks the value against the key and the entry, then writes it.</summary>
public sealed class SetSankeyPropertyCommandHandler(ISankeyDocumentStore documents) : ICommandHandler<SetSankeyPropertyCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetSankeyPropertyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var value = command.Value.Trim();
        double? number = null;
        if (command.Key is SankeyKeys.Value or SankeyKeys.Step && value.Length > 0)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed < 0)
            {
                return Task.FromResult(CommandResult.Failure($"'{value}' is not an amount this diagram can hold: a number of zero or more."));
            }

            if (command.Key == SankeyKeys.Step && parsed == 0)
            {
                return Task.FromResult(CommandResult.Failure("A step of zero would make + and − do nothing."));
            }

            number = parsed;
        }

        if (command.Key == SankeyKeys.Color && !SankeyColors.IsKnown(value))
        {
            return Task.FromResult(CommandResult.Failure($"`{value}` is not a colour: one of {string.Join(", ", SankeyColors.Palette)}, or #rrggbb."));
        }

        int? column = null;
        if (command.Key == SankeyKeys.Column && value.Length > 0)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
            {
                return Task.FromResult(CommandResult.Failure($"'{value}' is not a column: a whole number from 1, or nothing to let the flows decide."));
            }

            column = parsed;
        }

        return SankeyEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (SankeyEdits.NodeOf(model, command.EntryId) is { } node)
            {
                return command.Key switch
                {
                    SankeyKeys.Name => value.Length > 0
                        ? SankeyWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: false)
                        : SankeyEdit.Refused("A node needs a name."),
                    SankeyKeys.Color or SankeyKeys.Note or SankeyKeys.Format or SankeyKeys.Description =>
                        SankeyWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: true),
                    SankeyKeys.Column => SankeyWriter.SetNumber(document, node.Range, command.Key, column),
                    _ => Unknown(command.Key),
                };
            }

            if (SankeyEdits.FlowOf(model, command.EntryId) is { } flow)
            {
                return command.Key switch
                {
                    SankeyKeys.Color or SankeyKeys.Description => SankeyWriter.SetText(document, flow.Range, command.Key, value, removeWhenEmpty: true),
                    SankeyKeys.Value or SankeyKeys.Step => SankeyWriter.SetNumber(document, flow.Range, command.Key, number),
                    _ => Unknown(command.Key),
                };
            }

            return SankeyEdits.Gone();
        });
    }

    private static SankeyEdit Unknown(string key) => SankeyEdit.Refused($"`{key}` cannot be set on this entry.");
}
