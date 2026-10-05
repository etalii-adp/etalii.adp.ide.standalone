using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Changes a trend's span, scaling its dragged boundaries with it, or a trigger's date.</summary>
public sealed class SetGhgSpanCommandHandler(IGhgDocumentStore documents) : ICommandHandler<SetGhgSpanCommand>
{
    private static readonly string[] BoundaryAttributes = ["peakEnd", "troughEnd", "slopeEnd"];

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetGhgSpanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (GhgEdits.TriggerOf(model, command.TrendId) is { } trigger)
            {
                return GhgScale.ParseMonth(command.Start ?? command.Stop) is { } date
                    ? GhgWriter.SetPlacement(document, trigger, date, trigger.Row)
                    : GhgEdit.Refused($"'{command.Start ?? command.Stop}' is not a date; write it as YYYY-MM, such as 1947-12.");
            }

            if (GhgEdits.TrendOf(model, command.TrendId) is not { } trend)
            {
                return GhgEdits.Gone();
            }

            if (!trend.HasSpan)
            {
                return GhgEdits.NoSpan();
            }

            int? start = command.Start is null ? trend.Start : GhgScale.ParseMonth(command.Start);
            int? stop = command.Stop is null ? trend.Stop : GhgScale.ParseMonth(command.Stop);
            if (start is null || stop is null)
            {
                return GhgEdit.Refused($"'{command.Start ?? command.Stop}' is not a date; write it as YYYY-MM, such as 2007-06.");
            }

            if (stop <= start)
            {
                return GhgEdit.Refused("A trend must stop after it starts, at least one month later.");
            }

            if (GhgEdits.TooShort(stop.Value - start.Value, trend.VisiblePhases) is { } tooShort)
            {
                return tooShort;
            }

            // The stored boundaries follow the span by the definition's rescaleBoundaries hook, then the
            // host's clamp keeps every visible phase a month long (x-bounds.neighbour, still code).
            if (GhgDefinition.ElementOf(document.Disl.Diagram, trend.Id) is not { } element)
            {
                return GhgEdits.Gone();
            }

            var old = HookRunner.Snapshot(element);
            HookRunner.Store(element, "start", (long)start.Value);
            HookRunner.Store(element, "stop", (long)stop.Value);
            var hooks = HookRunner.AfterChange(GhgDefinition.Specification, element, old, ["start", "stop"], DislIds.Fixed(), GhgDefinition.EditEnv);
            if (!hooks.WasApplied)
            {
                return GhgEdit.Refused(hooks.Refusal!);
            }

            int?[] scaled = [.. BoundaryAttributes.Select(name => element.Attributes.TryGetValue(name, out var month) && month is long value ? (int?)value : null)];
            var dragged = GhgPhases.KeptAMonthApart(scaled, start.Value, stop.Value, trend.VisiblePhases);
            return GhgWriter.SetSpan(document, trend, start.Value, stop.Value, trend.Row, dragged);
        });
    }
}
