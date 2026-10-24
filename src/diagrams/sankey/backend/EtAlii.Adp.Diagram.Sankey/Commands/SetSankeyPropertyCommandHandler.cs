using System.Globalization;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Checks the value against the key and the entry, then writes it.</summary>
public sealed class SetSankeyPropertyCommandHandler(ISankeyDocumentStore documents) : SankeyEditHandler<SetSankeyPropertyCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(SetSankeyPropertyCommand command, CancellationToken cancellationToken) =>
        Read(command, out _, out _, out _) is { } refusal ? CommandResult.Failure(refusal) : await base.RunAsync(command, cancellationToken);

    /// <inheritdoc />
    protected override EditOutcome Apply(SankeyBody document, SankeyModel model, SetSankeyPropertyCommand command)
    {
        _ = Read(command, out var value, out var number, out var column);

        if (model.NodeOf(command.EntryId) is { } node)
        {
            return command.Key switch
            {
                SankeyKeys.Name => value.Length > 0
                    ? SankeyEdits.SetText(document, node, command.Key, value, removeWhenEmpty: false)
                    : EditOutcome.Refused("A node needs a name."),
                SankeyKeys.Color or SankeyKeys.Note or SankeyKeys.Format or SankeyKeys.Description =>
                    SankeyEdits.SetText(document, node, command.Key, value, removeWhenEmpty: true),
                SankeyKeys.Column => SankeyEdits.SetNumber(document, node, command.Key, column),
                _ => Unknown(command.Key),
            };
        }

        if (model.FlowOf(command.EntryId) is { } flow)
        {
            return command.Key switch
            {
                SankeyKeys.Color or SankeyKeys.Description => SankeyEdits.SetText(document, flow, command.Key, value, removeWhenEmpty: true),
                SankeyKeys.Value or SankeyKeys.Step => SankeyEdits.SetNumber(document, flow, command.Key, number),
                _ => Unknown(command.Key),
            };
        }

        return Gone;
    }

    /// <summary>
    /// Reads the value <paramref name="command"/> sets as the key it names, or says why it cannot be
    /// one; checked before the document is read, and read again when the edit is applied.
    /// </summary>
    private static string? Read(SetSankeyPropertyCommand command, out string value, out double? number, out int? column)
    {
        value = command.Value.Trim();
        number = null;
        column = null;
        if (command.Key is SankeyKeys.Value or SankeyKeys.Step && value.Length > 0)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed < 0)
            {
                return $"'{value}' is not an amount this diagram can hold: a number of zero or more.";
            }

            if (command.Key == SankeyKeys.Step && parsed == 0)
            {
                return "A step of zero would make + and − do nothing.";
            }

            number = parsed;
        }

        if (command.Key == SankeyKeys.Color && !SankeyColors.IsKnown(value))
        {
            return $"`{value}` is not a colour: one of {string.Join(", ", SankeyColors.Palette)}, or #rrggbb.";
        }

        if (command.Key == SankeyKeys.Column && value.Length > 0)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
            {
                return $"'{value}' is not a column: a whole number from 1, or nothing to let the flows decide.";
            }

            column = parsed;
        }

        return null;
    }

    private static EditOutcome Unknown(string key) => EditOutcome.Refused($"`{key}` cannot be set on this entry.");
}
