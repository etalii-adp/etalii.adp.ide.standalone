using EtAlii.Adp.Common;
using Serilog;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Moves one element to a position in the document's own axis order (Requirement 7.2).
/// </summary>
/// <remarks>
/// A drag is a <b>document edit</b>, not a view change. Position is meaning on a Wardley map -
/// moving a component right asserts that it is more evolved - so this goes through the
/// project's history like every other edit, is written back to the `.owm`, and reaches every
/// other connection as ordinary deltas.
/// </remarks>
/// <param name="BodyPath">The `.owm` file to edit.</param>
/// <param name="ElementId">The element being moved.</param>
/// <param name="Visibility">Where it lands on the value-chain axis.</param>
/// <param name="Maturity">Where it lands on the evolution axis.</param>
public sealed record MoveWardleyElementCommand(
    string BodyPath,
    string ElementId,
    double Visibility,
    double Maturity) : ICommand;

/// <summary>
/// Applies a move by rewriting the one statement it occupies, and reports the command that puts
/// it back.
/// </summary>
public sealed class MoveWardleyElementCommandHandler : ICommandHandler<MoveWardleyElementCommand>
{
    private static readonly ILogger _logger = Log.ForContext<MoveWardleyElementCommandHandler>();

    private readonly IWardleyDocumentStore _documents;

    public MoveWardleyElementCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(MoveWardleyElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var document = _documents.GetOrLoad(command.BodyPath);
        var map = WardleyParser.Parse(document);

        // From the store, not reconciled here: an unmatched element gets a FRESH ShortGuid, so
        // reconciling separately would mint ids the session never handed out and this handler
        // would never find the element it was asked to move.
        var identities = _documents.Identities(command.BodyPath);

        // Preconditions are checked against CURRENT state rather than trusted from when the
        // command was made, because undo and redo dispatch this again later (Requirement 9.5).
        var target = Locate(map, identities, command.ElementId);
        if (target is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer on this map."));
        }

        var clamped = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));

        // Captured before the edit, so the inverse restores the LINE rather than the numbers.
        // Recomputing from numbers would bring `[0.90, 0.10]` back as `[0.9, 0.1]` - the same
        // position, a different file - and an undo has to leave no trace at all.
        var line = target.Value.Child?.Line ?? target.Value.Component!.Line;
        var before = document.Lines[(int)line - 1];

        if (target.Value.Child is { } child)
        {
            // A pipeline child has no visibility of its own - the format gives it nowhere to
            // put one - so only its maturity moves (Requirement 7.4).
            if (!WardleyWriter.SetPipelineChildMaturity(document, child, clamped.Maturity))
            {
                return Task.FromResult(CommandResult.Failure("That element could not be moved."));
            }
        }
        else
        {
            var component = target.Value.Component!;
            if (!WardleyWriter.SetPosition(document, component, clamped))
            {
                return Task.FromResult(CommandResult.Failure("That element could not be moved."));
            }

            _logger.Debug("Moved {Name} to {Visibility},{Maturity}", component.Name, clamped.Visibility, clamped.Maturity);
        }

        var published = _documents.Save(command.BodyPath);
        return Task.FromResult(published.Error.Length == 0
            ? CommandResult.Success( new RestoreWardleyLineCommand(command.BodyPath, line, before), published.Warning)
            : CommandResult.Failure(published.Error));
    }

    /// <summary>The component or pipeline child <paramref name="elementId"/> names, or null.</summary>
    private static (WardleyComponent? Component, WardleyPipelineChild? Child)? Locate(
        WardleyMap map,
        IReadOnlyList<WardleyIdentityEntry> identities,
        string elementId)
    {
        var entry = identities.FirstOrDefault(candidate => candidate.Id == elementId);
        if (entry is null)
        {
            return null;
        }

        if (entry.Kind == WardleyIdentityKind.Component)
        {
            var component = map.Components.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == entry.Key);
            return component is null ? null : (component, null);
        }

        if (entry.Kind != WardleyIdentityKind.PipelineChild)
        {
            return null;
        }

        foreach (var pipeline in map.Pipelines)
        {
            foreach (var child in pipeline.Children)
            {
                if (WardleyIdentityKeys.Of(pipeline, child) == entry.Key)
                {
                    return (null, child);
                }
            }
        }

        return null;
    }
}
