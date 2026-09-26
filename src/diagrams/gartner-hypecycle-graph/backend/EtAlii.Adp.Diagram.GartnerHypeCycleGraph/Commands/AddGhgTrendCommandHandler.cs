using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Adds a trend one year long from the snapped drop month, with all four phases.</summary>
public sealed class AddGhgTrendCommandHandler(IGhgDocumentStore documents) : ICommandHandler<AddGhgTrendCommand>
{
    /// <summary>A new trend's length in months.</summary>
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
            if (model.Trends.Any(trend => trend.Id == minted.TrendId) ||
                model.Influences.Any(influence => influence.Id == minted.TrendId))
            {
                return GhgEdit.Refused("That id is already used in this graph.");
            }

            var start = GhgScale.MonthContaining(minted.X);
            var trend = new GhgTrend(
                minted.TrendId,
                UniqueName(model),
                start,
                start + DefaultMonths,
                GhgScale.RowAtMiddle(minted.Y),
                GhgPhases.Count,
                [null, null, null],
                [],
                Description: "",
                Range: new LineRange(0, 0));

            return GhgWriter.AddTrend(document, model, trend);
        });
    }

    /// <summary>The default name, or the name with the lowest number that makes it unique.</summary>
    private static string UniqueName(GhgModel model)
    {
        var taken = model.Trends.Select(trend => trend.Name).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(DefaultName))
        {
            return DefaultName;
        }

        for (var number = 2; ; number++)
        {
            var candidate = $"{DefaultName} {number}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
