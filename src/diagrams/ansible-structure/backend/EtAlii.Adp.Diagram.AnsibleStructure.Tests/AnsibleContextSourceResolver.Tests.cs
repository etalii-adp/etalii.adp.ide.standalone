using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// Making a node selectable - and refusing to record a selection that cannot be verified.
/// </summary>
public class AnsibleContextSourceResolverTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private readonly string _root;
    private readonly string _registration;
    private readonly AnsibleProjectStore _store = new(new AnsibleProjectReader(), SettleDelay);
    private readonly AnsibleContextSourceResolver _resolver;

    public AnsibleContextSourceResolverTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        CopyTree(IoPath.Combine("Fixtures", "infrastructure"), _root);
        _registration = IoPath.Combine(_root, "infrastructure.adp");
        File.WriteAllText(_registration, "ansible/structure\n");

        // A mindmap definition beside this module's own, so the "not ours" path has a real
        // other type to be rejected in favour of rather than a hypothetical one.
        var catalog = new AnsibleTestDiagramDefinitionCatalog(
            Diagram.AnsibleStructure,
            new DiagramDefinition(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm"));
        _resolver = new AnsibleContextSourceResolver(new DiagramFileRouter(catalog), _store);
    }

    public void Dispose()
    {
        _store.Dispose();
        TestFolder.TryDelete(_root);
    }

    // ---- resolving ----------------------------------------------------------------------------

    [Fact]
    public void ItAnswersForElementIds_AndNothingElse()
    {
        // Arrange, act and assert.
        Assert.True(_resolver.CanResolve(new ContextSource { ElementId = new ElementId { Value = "role:nginx" } }));
        Assert.False(_resolver.CanResolve(new ContextSource { EntryId = ShortGuid.NewShortGuid() }));
    }

    [Fact]
    public async Task ANode_ResolvesToItsOwnPathAndName()
    {
        // Act.
        var resolution = await Resolve("role:nginx");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
        Assert.Equal(["roles", "nginx"], level.RelativePath);
        Assert.Equal("nginx", level.Detail.Element.Text);
        Assert.Equal("role:nginx", level.Target.ElementId);
    }

    [Fact]
    public async Task AnEdge_ResolvesAsItsDeclaringSide()
    {
        // Arrange.
        // Requirement 8.3: an edge's selection names where it was written, because that is the
        // file a reader asking "why is this here" has to open.
        var project = _store.GetOrLoad(_root);
        var edge = AnsibleGraph.Derive(project).Edges.Single(e => e.Kind == AnsibleEdgeKind.IncludesTasks);

        // Act.
        var resolution = await Resolve(edge.Id);

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["roles", "nginx", "tasks", "main.yml"], level.RelativePath);
        Assert.Contains("include_tasks:", level.Detail.Element.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingNestsInsideANode()
    {
        // Act.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve("role:nginx")).Level;

        // Assert.
        Assert.Equal(ContextNesting.NotNestable, _resolver.NestingOf(level));
    }

    // ---- what it refuses -------------------------------------------------------------------------

    [Fact]
    public async Task AnElementWithNoDiagramAboveIt_IsRejected()
    {
        // Act.
        // An unverifiable selection is never recorded.
        var resolution = await _resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), _root, ContextSelectionSource.DiagramCanvas,
            new ContextSource { ElementId = new ElementId { Value = "role:nginx" } },
            [], parent: null, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("within its diagram", Assert.IsType<RejectedContextLevel>(resolution).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownElement_IsRejected()
    {
        // Act.
        var resolution = await Resolve("role:no-such-role");

        // Assert.
        Assert.Equal("Unknown element.", Assert.IsType<RejectedContextLevel>(resolution).Reason);
    }

    [Fact]
    public async Task AClientPathThatDisagreesWithTheElement_IsRejected()
    {
        // Act.
        // The client's version of the path is checked, never trusted.
        var resolution = await Resolve("role:nginx", clientPath: ["roles", "postgres"]);

        // Assert.
        Assert.Contains("does not match", Assert.IsType<RejectedContextLevel>(resolution).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileOfAnotherType_IsRejectedPlainly()
    {
        // Arrange.
        // Several resolvers share the element_id member; saying "not ours" plainly is how they
        // coexist without fighting over a selection.
        var other = IoPath.Combine(_root, "map.adp");
        await File.WriteAllTextAsync(other, "freeplane/mindmap\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "map.mm"), "<map><node TEXT=\"a\"/></map>", TestContext.Current.CancellationToken);

        // Act.
        var resolution = await Resolve("role:nginx", registration: other);

        // Assert.
        Assert.Contains("not an Ansible structure diagram", Assert.IsType<RejectedContextLevel>(resolution).Reason, StringComparison.Ordinal);
    }

    // ---- tracking ---------------------------------------------------------------------------------

    [Fact]
    public async Task ADeletedRole_ClearsTheSelection()
    {
        // Arrange.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve("role:postgres")).Level;
        var cleared = new TaskCompletionSource<bool>();
        using var subscription = _resolver.Track(ShortGuid.NewShortGuid(), _root, level, path =>
        {
            if (path is null)
            {
                cleared.TrySetResult(true);
            }
        });

        // Act.
        Directory.Delete(IoPath.Combine(_root, "roles", "postgres"), recursive: true);

        // Assert.
        Assert.True(await cleared.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADisposedSubscription_StopsAnnouncing()
    {
        // Arrange.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve("role:postgres")).Level;
        var announcements = 0;
        var subscription = _resolver.Track(ShortGuid.NewShortGuid(), _root, level, _ => Interlocked.Increment(ref announcements));

        // Act.
        subscription.Dispose();
        Directory.Delete(IoPath.Combine(_root, "roles", "postgres"), recursive: true);
        await Task.Delay(SettleDelay + SettleDelay + SettleDelay, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(0, announcements);
    }

    // ---- plumbing -----------------------------------------------------------------------------------

    private async Task<ContextLevelResolution> Resolve(string elementId, IReadOnlyList<string>? clientPath = null, string? registration = null)
    {
        var parent = new ContextResolvedLevel(
            ContextSelectionSource.Explorer,
            new ContextSource { EntryId = ShortGuid.NewShortGuid() },
            [],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, registration ?? _registration, IsContainer: false, SourceId: default, _root),
            new ContextLevelDetail(),
            null!);

        return await _resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), _root, ContextSelectionSource.DiagramCanvas,
            new ContextSource { ElementId = new ElementId { Value = elementId } },
            clientPath ?? [], parent, TestContext.Current.CancellationToken);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var folder in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(IoPath.Combine(destination, IoPath.GetRelativePath(source, folder)));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, IoPath.Combine(destination, IoPath.GetRelativePath(source, file)));
        }
    }
}
