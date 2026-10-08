using System.Globalization;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Checks the value against the key and the entry, then writes it.</summary>
public sealed class SetSupplyChainPropertyCommandHandler(ISupplyChainDocumentStore documents) : ICommandHandler<SetSupplyChainPropertyCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetSupplyChainPropertyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var value = command.Value.Trim();
        double? number = null;
        if (SupplyChainKeys.Numbers.Contains(command.Key, StringComparer.Ordinal) && value.Length > 0)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed < 0)
            {
                return Task.FromResult(CommandResult.Failure($"'{value}' is not an amount this diagram can hold: a number of zero or more."));
            }

            if (command.Key == SupplyChainKeys.Step && parsed == 0)
            {
                return Task.FromResult(CommandResult.Failure("A step of zero would make + and − do nothing."));
            }

            number = parsed;
        }

        return SupplyChainEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (SupplyChainEdits.NodeOf(model, command.EntryId) is { } node)
            {
                return command.Key switch
                {
                    SupplyChainKeys.Name => SupplyChainWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: false),
                    SupplyChainKeys.Description or SupplyChainKeys.Unit => SupplyChainWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: true),
                    SupplyChainKeys.Quantity or SupplyChainKeys.Step => SupplyChainWriter.SetNumber(document, node.Range, command.Key, number),
                    SupplyChainKeys.Type => SupplyChainNodeTypes.IsKnown(value)
                        ? SupplyChainWriter.SetText(document, node.Range, command.Key, SupplyChainNodeTypes.Normalize(value), removeWhenEmpty: false)
                        : SupplyChainEdit.Refused($"`{value}` is not a stage: {string.Join(", ", SupplyChainNodeTypes.All)}."),
                    SupplyChainKeys.Group => value.Length == 0 || SupplyChainEdits.GroupOf(model, value) is not null
                        ? SupplyChainWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: true)
                        : SupplyChainEdit.Refused($"There is no group `{value}` in this diagram."),
                    _ => Unknown(command.Key),
                };
            }

            if (SupplyChainEdits.FlowOf(model, command.EntryId) is { } flow)
            {
                return command.Key switch
                {
                    SupplyChainKeys.Product or SupplyChainKeys.Description or SupplyChainKeys.Unit => SupplyChainWriter.SetText(document, flow.Range, command.Key, value, removeWhenEmpty: true),
                    SupplyChainKeys.Volume or SupplyChainKeys.Step => SupplyChainWriter.SetNumber(document, flow.Range, command.Key, number),
                    _ => Unknown(command.Key),
                };
            }

            if (SupplyChainEdits.GroupOf(model, command.EntryId) is { } group)
            {
                return command.Key switch
                {
                    SupplyChainKeys.Name => SupplyChainWriter.SetText(document, group.Range, command.Key, value, removeWhenEmpty: false),
                    SupplyChainKeys.Description => SupplyChainWriter.SetText(document, group.Range, command.Key, value, removeWhenEmpty: true),
                    _ => Unknown(command.Key),
                };
            }

            return SupplyChainEdits.Gone();
        });
    }

    private static SupplyChainEdit Unknown(string key) => SupplyChainEdit.Refused($"`{key}` cannot be set on this entry.");
}
