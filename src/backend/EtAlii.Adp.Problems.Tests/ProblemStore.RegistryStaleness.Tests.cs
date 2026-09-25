using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
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

    [Fact]
    public void AVersionedVerdict_GoesStale_OnceTheModuleThatJudgedItIsGone()
    {
        // Arrange.
        // The third case in the family, and the mirror of the first: there the registry gained
        // a type and a core verdict went on denying it; here the registry LOSES the type and a
        // module's verdict goes on asserting it. Nothing claims the file now, so there is no
        // rules version to compare and no authority behind the verdict - but the file it names
        // never moved, so the file stamp cannot notice either.
        CreatePair("services", "dotnet/dependency-graph", ".dgr");
        using (var whileTheModuleExisted = Store(MindmapDefinition, DependencyGraphDefinition))
        {
            whileTheModuleExisted.Replace(_root, [ModuleVerdict("services.adp", rulesVersion: "1.2.3")]);
        }

        // Act.
        // The module is removed - unregistered, uninstalled, or dropped from a build.
        using var afterItWasRemoved = Store(MindmapDefinition);
        var set = afterItWasRemoved.Get(_root);

        // Assert.
        var verdict = Assert.Single(set.Problems);
        Assert.True(
            verdict.Stale,
            "Nothing claims this type any more, so nothing stands behind the verdict - it must not read as current.");
    }

    [Fact]
    public void AVersionedVerdict_StaysFresh_WhileItsModuleIsStillThereAtTheSameVersion()
    {
        // Arrange.
        // The floor: the test above must not pass by marking every versioned verdict stale.
        CreatePair("services", "dotnet/dependency-graph", ".dgr");
        var version = new DiagramValidators([]).RulesVersion(DependencyGraph);
        using (var before = Store(MindmapDefinition, DependencyGraphDefinition))
        {
            before.Replace(_root, [ModuleVerdict("services.adp", version)]);
        }

        // Act.
        using var unchanged = Store(MindmapDefinition, DependencyGraphDefinition);
        var set = unchanged.Get(_root);

        // Assert.
        var verdict = Assert.Single(set.Problems);
        Assert.False(verdict.Stale, "The module is still there at the same version: the verdict still holds.");
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

    /// <summary>
    /// What a module's own validator produces, pinned to the file and carrying the rules
    /// version that judged it - so only the module's fate can make it stale.
    /// </summary>
    private StoredProblem ModuleVerdict(string relativePath, string rulesVersion)
    {
        var info = new FileInfo(IoPath.Combine(_root, relativePath));
        return new StoredProblem(
            new DiagramProblem(DiagramProblemSeverity.Error, "The graph names a project that is not in the solution.", "dependency-graph.missing-project"),
            relativePath,
            info.LastWriteTimeUtc,
            info.Length,
            rulesVersion);
    }
}
