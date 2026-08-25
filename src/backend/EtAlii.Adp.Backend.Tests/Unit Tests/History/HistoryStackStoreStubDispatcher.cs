using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>Every command succeeds and reports itself as its own inverse - enough to record and to raise Changed.</summary>
internal sealed class HistoryStackStoreStubDispatcher : ICommandDispatcher
{
    public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(CommandResult.Success(command));
}
