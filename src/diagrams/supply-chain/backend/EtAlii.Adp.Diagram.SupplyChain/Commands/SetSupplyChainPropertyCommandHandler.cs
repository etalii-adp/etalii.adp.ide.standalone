using System.Globalization;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Writes a value into a row of the entry's form, checked and written as the definition's form says.</summary>
public sealed class SetSupplyChainPropertyCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<SetSupplyChainPropertyCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>
    /// The value is the command's own, so a number that is not one, and a step of zero, are refused before the document is
    /// read, as they always were; the definition's rows refuse them too, in the same sentences.
    /// </remarks>
    protected override async Task<CommandResult> RunAsync(SetSupplyChainPropertyCommand command, CancellationToken cancellationToken) =>
        Refusal(command) is { } refusal ? CommandResult.Failure(refusal) : await base.RunAsync(command, cancellationToken);

    /// <inheritdoc />
    protected override EditOutcome Apply(SupplyChainBody document, SupplyChainModel model, SetSupplyChainPropertyCommand command) =>
        SupplyChainDefinition.Set(document, model, command.EntryId, command.Key, command.Value) ?? Gone;

    /// <summary>Why the value of <paramref name="command"/> cannot be set to any entry; null when it can be tried.</summary>
    private static string? Refusal(SetSupplyChainPropertyCommand command)
    {
        var value = command.Value.Trim();
        if (!SupplyChainKeys.Numbers.Contains(command.Key, StringComparer.Ordinal) || value.Length == 0)
        {
            return null;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed < 0)
        {
            return $"'{value}' is not an amount this diagram can hold: a number of zero or more.";
        }

        return command.Key == SupplyChainKeys.Step && parsed == 0 ? "A step of zero would make + and − do nothing." : null;
    }
}
