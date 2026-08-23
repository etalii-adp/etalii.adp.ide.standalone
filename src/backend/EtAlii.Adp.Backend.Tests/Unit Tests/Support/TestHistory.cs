using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A real <see cref="IHistoryStack"/> over the real dispatcher and the real handlers, built
/// from the same <c>AddCommands</c> the host uses.
/// </summary>
/// <remarks>
/// Deliberately not a stub. A context action provider's job is now to raise the right command
/// rather than to change anything itself, and a stub that records commands would let a
/// provider raise one no handler can carry out and still pass. Running the actual pipeline
/// keeps these tests checking what the user would get, and it is what lets them assert that
/// an action can be undone.
/// </remarks>
internal static class TestHistory
{
    public static IHistoryStack Create() => Services().GetRequiredService<IHistoryStack>();

    private static ServiceProvider Services() => new ServiceCollection().AddCommands().BuildServiceProvider();
}
