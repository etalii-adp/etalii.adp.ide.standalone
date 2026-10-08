using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// A real <see cref="IHistoryStackStore"/> over the real dispatcher and the real handlers,
/// built from the same <c>AddCommands</c> the host uses.
/// </summary>
/// <remarks>
/// Deliberately not a stub. A context action provider's job is now to raise the right command
/// rather than to change anything itself, and a stub that records commands would let a
/// provider raise one no handler can carry out and still pass. Running the actual pipeline
/// keeps these tests checking what the user would get, and it is what lets them assert that
/// an action can be undone.
/// </remarks>
public static class TestHistory
{
    /// <summary>
    /// The per-project store a provider now takes. A test hands this to the provider and
    /// reaches the stack it asserts on through <see cref="IHistoryStackStore.Get"/> with the
    /// same root path it put on its targets, so the two see the same history.
    /// </summary>
    private static IHistoryStackStore CreateStore(params DiagramDefinition[] definitions) =>
        Services(definitions).GetRequiredService<IHistoryStackStore>();

    /// <summary>
    /// The stack for a single project, for a test that only ever works within one root: it
    /// gets the store's stack for <paramref name="rootPath"/> and asserts undo/redo on it
    /// directly, while the provider it drives reaches the same stack through the store.
    /// </summary>
    public static IHistoryStack Create(string rootPath, out IHistoryStackStore store, params DiagramDefinition[] definitions)
    {
        store = CreateStore(definitions);
        return store.Get(rootPath);
    }

    /// <summary>
    /// The handlers ask the catalog whether a file is a diagram's registration file with a
    /// body sibling; a test that cares hands in the definitions it wants known, and one that
    /// does not gets an empty catalog rather than the process-wide cache.
    /// </summary>
    private static ServiceProvider Services(DiagramDefinition[] definitions) => new ServiceCollection()
        .AddSingleton<IDiagramDefinitionCatalog>(new TestDiagramDefinitionCatalog(definitions))
        .AddCommands().AddHierarchyCommandHandlers()
        .BuildServiceProvider();

}
