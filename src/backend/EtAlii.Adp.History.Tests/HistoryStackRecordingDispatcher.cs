namespace EtAlii.Adp.History.Tests;

/// <summary>
/// Applies <see cref="HistoryStackSetCommand"/>s to an in-memory value, recording the order every
/// command was seen in so tests can assert on what actually ran, not just on counts.
/// </summary>
internal sealed class HistoryStackRecordingDispatcher : ICommandDispatcher
{
    private readonly List<ICommand> _dispatched = [];

    public IReadOnlyList<ICommand> Dispatched => _dispatched;

    public string Value { get; private set; } = "initial";

    /// <summary>Set to have the next dispatch of a <see cref="HistoryStackSetCommand"/> fail.</summary>
    public string? FailNextWith { get; set; }

    /// <summary>The document the next dispatch reports as changed by another program.</summary>
    public string? OutdatedNextFor { get; set; }

    public Func<Task>? BeforeEachDispatch { get; set; }

    public async Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        if (BeforeEachDispatch is not null)
        {
            await BeforeEachDispatch();
        }

        _dispatched.Add(command);

        if (FailNextWith is { } error)
        {
            FailNextWith = null;
            return CommandResult.Failure(error);
        }

        if (OutdatedNextFor is { } bodyPath)
        {
            OutdatedNextFor = null;
            return CommandResult.Outdated("changed by another program", bodyPath);
        }

        return command switch
        {
            HistoryStackSetCommand set => Apply(set),
            HistoryStackBoundCommand bound => Apply(bound),
            HistoryStackFailingCommand failing => CommandResult.Failure(failing.Error),
            _ => CommandResult.Success(),
        };
    }

    private CommandResult Apply(HistoryStackBoundCommand command)
    {
        var previous = Value;
        Value = command.Value;
        return CommandResult.Success(new HistoryStackBoundCommand(command.BodyPath, previous));
    }

    private CommandResult Apply(HistoryStackSetCommand command)
    {
        var previous = Value;
        Value = command.Value;
        return CommandResult.Success(new HistoryStackSetCommand(previous));
    }
}
