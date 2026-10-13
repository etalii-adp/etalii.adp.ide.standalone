using System.Globalization;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Checks the value against the key and the entry, then writes it.</summary>
public sealed class SetSupplyChainPropertyCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<SetSupplyChainPropertyCommand>(documents)
{
    /// <inheritdoc />
    protected override CommandResult Execute(SetSupplyChainPropertyCommand command) =>
        Read(command, out _, out _) is { } refusal ? CommandResult.Failure(refusal) : base.Execute(command);

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, SetSupplyChainPropertyCommand command)
    {
        Read(command, out var value, out var number);
        if (model.NodeOf(command.EntryId) is { } node)
        {
            return command.Key switch
            {
                SupplyChainKeys.Name => SupplyChainWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: false),
                SupplyChainKeys.Description or SupplyChainKeys.Unit => SupplyChainWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: true),
                SupplyChainKeys.Quantity or SupplyChainKeys.Step => SupplyChainWriter.SetNumber(document, node.Range, command.Key, number),
                SupplyChainKeys.Type => SupplyChainNodeTypes.IsKnown(value)
                    ? SupplyChainWriter.SetText(document, node.Range, command.Key, SupplyChainNodeTypes.Normalize(value), removeWhenEmpty: false)
                    : EditOutcome.Refused($"`{value}` is not a stage: {string.Join(", ", SupplyChainNodeTypes.All)}."),
                SupplyChainKeys.Group => value.Length == 0 || model.GroupOf(value) is not null
                    ? SupplyChainWriter.SetText(document, node.Range, command.Key, value, removeWhenEmpty: true)
                    : EditOutcome.Refused($"There is no group `{value}` in this diagram."),
                _ => Unknown(command.Key),
            };
        }

        if (model.FlowOf(command.EntryId) is { } flow)
        {
            return command.Key switch
            {
                SupplyChainKeys.Product or SupplyChainKeys.Description or SupplyChainKeys.Unit => SupplyChainWriter.SetText(document, flow.Range, command.Key, value, removeWhenEmpty: true),
                SupplyChainKeys.Volume or SupplyChainKeys.Step => SupplyChainWriter.SetNumber(document, flow.Range, command.Key, number),
                _ => Unknown(command.Key),
            };
        }

        if (model.GroupOf(command.EntryId) is { } group)
        {
            return command.Key switch
            {
                SupplyChainKeys.Name => SupplyChainWriter.SetText(document, group.Range, command.Key, value, removeWhenEmpty: false),
                SupplyChainKeys.Description => SupplyChainWriter.SetText(document, group.Range, command.Key, value, removeWhenEmpty: true),
                _ => Unknown(command.Key),
            };
        }

        return Gone;
    }

    /// <summary>The trimmed value and, for a number key, the number it reads as; or why it cannot be set.</summary>
    private static string? Read(SetSupplyChainPropertyCommand command, out string value, out double? number)
    {
        value = command.Value.Trim();
        number = null;
        if (SupplyChainKeys.Numbers.Contains(command.Key, StringComparer.Ordinal) && value.Length > 0)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed < 0)
            {
                return $"'{value}' is not an amount this diagram can hold: a number of zero or more.";
            }

            if (command.Key == SupplyChainKeys.Step && parsed == 0)
            {
                return "A step of zero would make + and − do nothing.";
            }

            number = parsed;
        }

        return null;
    }

    private static EditOutcome Unknown(string key) => EditOutcome.Refused($"`{key}` cannot be set on this entry.");
}
