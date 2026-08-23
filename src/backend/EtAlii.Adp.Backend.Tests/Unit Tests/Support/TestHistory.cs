using EtAlii.Adp.Diagram;
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
    public static IHistoryStack Create(params DiagramDefinition[] definitions) =>
        Services(definitions).GetRequiredService<IHistoryStack>();

    /// <summary>
    /// The handlers ask the catalog whether a file is a diagram's registration file with a
    /// body sibling; a test that cares hands in the definitions it wants known, and one that
    /// does not gets an empty catalog rather than the process-wide cache.
    /// </summary>
    private static ServiceProvider Services(DiagramDefinition[] definitions) => new ServiceCollection()
        .AddSingleton<IDiagramDefinitionCatalog>(new TestCatalog(definitions))
        .AddCommands()
        .BuildServiceProvider();

    private sealed class TestCatalog(IReadOnlyList<DiagramDefinition> definitions) : IDiagramDefinitionCatalog
    {
        public IReadOnlyList<DiagramDefinition> All { get; } = definitions;
    }
}
