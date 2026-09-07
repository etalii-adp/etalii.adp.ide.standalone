using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Problems.Tests;

/// <summary>
/// A core verdict about ROUTING - "not a known diagram type", "claimed by more than one
/// diagram type" - is a claim about the diagram-type registry, not about the file. Adding a
/// module makes such a verdict false while the file it names never changes, so neither the
/// file stamp nor the rules version can notice: the entry outlives a rebuild and a restart
/// and is presented with the authority of a current one.
/// </summary>
/// <remarks>
/// The fixture owns a cache ACROSS a simulated restart - two stores over one appdata root,
/// the second holding the catalog the module was added to. The integration suites cannot
/// reach this class by construction: each replaces <see cref="IProblemStore"/> with a
/// temp-rooted one and therefore starts with an empty cache, deliberately, because 605
/// stale cache files once cost that suite 22,591 warnings. Nothing here weakens that
/// isolation; it tests the lifetime those suites give up.
/// </remarks>
public class ProblemStoreRegistryStalenessTests : IDisposable
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", Extension: ".mm");
    private static readonly DiagramOrigin DependencyGraph = new("dotnet", "dependency-graph");
    private static readonly DiagramDefinition DependencyGraphDefinition = new(DependencyGraph, "Dependency graph", Extension: ".dgr");

    private readonly string _appData;
    private readonly string _root;

    public ProblemStoreRegistryStalenessTests()
    {
        var scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        _appData = IoPath.Combine(scratch, "appdata");
        _root = IoPath.Combine(scratch, "project");
        Directory.CreateDirectory(_appData);
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => TestFolder.TryDelete(IoPath.GetDirectoryName(_root)!);

    [Fact]
    public void AnUnknownTypeVerdict_GoesStale_OnceTheModuleProvidingThatTypeIsDiscovered()
    {
        // Arrange.
        // The project holds a diagram of a type no module claims yet, and the verdict core
        // reaches for it is cached and persisted.
        CreatePair("services", "dotnet/dependency-graph", ".dgr");
        using (var beforeTheModuleExisted = Store(MindmapDefinition))
        {
            beforeTheModuleExisted.Replace(_root, [UnknownTypeVerdict("services.adp")]);
        }

        // Act.
        // The module is built and discovered; the backend restarts and reads the same cache.
        using var afterItWasDiscovered = Store(MindmapDefinition, DependencyGraphDefinition);
        var set = afterItWasDiscovered.Get(_root);

        // Assert.
        // The file never changed, so nothing about it can carry the news. The registry did.
        var verdict = Assert.Single(set.Problems);
        Assert.True(
            verdict.Stale,
            "The type is known now, so 'not a known diagram type' is false - the panel must not present it as current.");
    }

    [Fact]
    public void AnUnknownTypeVerdict_StaysFresh_WhileTheTypeIsStillUnknown()
    {
        // Arrange.
        // The floor for the test above: it must not pass by marking every core verdict stale.
        CreatePair("services", "dotnet/dependency-graph", ".dgr");
        using (var before = Store(MindmapDefinition))
        {
            before.Replace(_root, [UnknownTypeVerdict("services.adp")]);
        }

        // Act.
        using var stillWithoutTheModule = Store(MindmapDefinition);
        var set = stillWithoutTheModule.Get(_root);

        // Assert.
        var verdict = Assert.Single(set.Problems);
        Assert.False(verdict.Stale, "Nothing changed: the type is still unknown and the verdict still holds.");
    }

    private ProblemStore Store(params DiagramDefinition[] definitions) =>
        new(_appData,
            new DiagramFileRouter(new TestDiagramDefinitionCatalog(definitions)),
            new DiagramValidators([]),
            writeDelay: TimeSpan.FromMilliseconds(1));

    private void CreatePair(string baseName, string mimeType, string bodyExtension)
    {
        File.WriteAllText(IoPath.Combine(_root, baseName + ".adp"), mimeType + "\n");
        File.WriteAllText(IoPath.Combine(_root, baseName + bodyExtension), "the document");
    }

    /// <summary>
    /// What <c>ProjectValidator</c> produces for an unroutable file, pinned to the file as it
    /// is on disk - so the file stamp cannot be what makes it stale.
    /// </summary>
    private StoredProblem UnknownTypeVerdict(string relativePath)
    {
        var info = new FileInfo(IoPath.Combine(_root, relativePath));
        return new StoredProblem(
            new DiagramProblem(DiagramProblemSeverity.Error, "'dotnet/dependency-graph' is not a known diagram type.", "core.unknown-type"),
            relativePath,
            info.LastWriteTimeUtc,
            info.Length,
            RulesVersion: "");
    }
}
