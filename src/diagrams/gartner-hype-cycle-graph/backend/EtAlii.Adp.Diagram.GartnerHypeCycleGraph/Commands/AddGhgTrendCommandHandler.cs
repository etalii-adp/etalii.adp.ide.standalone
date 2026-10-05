using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Adds a trend twelve steps of the diagram's time unit long - a year in a diagram of months - from
/// the start of the step it was dropped in, with all four phases: the definition's
/// <c>addTrendHere</c> operation, run by <see cref="OperationInterpreter"/>.
/// </summary>
public sealed class AddGhgTrendCommandHandler(IGhgDocumentStore documents) : ICommandHandler<AddGhgTrendCommand>
{
    /// <summary>A new trend's length in steps of the diagram's time unit; months, in a diagram of months.</summary>
    public const int DefaultMonths = 12;

    /// <summary>A new trend's name, numbered when it is taken.</summary>
    public const string DefaultName = "New trend";

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddGhgTrendCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        // Minted once and carried as the redo, so a redone trend keeps its id.
        var minted = command.TrendId.Length > 0
            ? command
            : command with { TrendId = ShortGuid.NewShortGuid().ToString() };

        return GhgEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            if (GhgEdits.IdTaken(model, minted.TrendId))
            {
                return GhgEdit.Refused("That id is already used in this graph.");
            }

            // The definition's addTrendHere, as the toolbox's trend drop: its name, its span and its row.
            return GhgDefinition.Apply(document, OperationInterpreter.Run(
                GhgDefinition.Specification,
                "addTrendHere",
                document.Disl.Diagram,
                null,
                DislIds.Fixed(minted.TrendId),
                new DislInvocation(GhgDefinition.Position(minted.X, minted.Y, model.TimeUnit)),
                GhgDefinition.EditEnv));
        });
    }
}
