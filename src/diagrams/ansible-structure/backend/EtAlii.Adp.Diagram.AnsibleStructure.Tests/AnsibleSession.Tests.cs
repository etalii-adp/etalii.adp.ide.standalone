using EtAlii.Adp.Backend.Diagrams;

using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// One connection's live view of one folder: the baseline, the viewport, the push when the tree
/// changes - and the one thing it refuses.
/// </summary>
public class AnsibleSessionTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private readonly string _root;
    private readonly AnsibleProjectStore _store = new(new AnsibleProjectReader(), SettleDelay);
    private readonly AnsibleSessionFactory _factory;

    public AnsibleSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        CopyTree(IoPath.Combine("Fixtures", "infrastructure"), _root);
        // The registration a real Add would have written. Nothing else is added to the folder.
        File.WriteAllText(IoPath.Combine(_root, "infrastructure.adp"), "ansible/structure\n");
        _factory = new AnsibleSessionFactory(_store, new AnsibleElementMapper());
    }

    public void Dispose()
    {
        _store.Dispose();
        TestFolder.TryDelete(_root);
    }

    private IDiagramSession Open() =>
        _factory.Open(ShortGuid.NewShortGuid(), _root, IoPath.Combine(_root, "infrastructure.adp"), IoPath.Combine(_root, "infrastructure.adp"));

    // ---- the subject is the folder --------------------------------------------------------

    [Fact]
    public async Task TheFactory_ResolvesTheSubjectAsTheFolderTheRegistrationSitsIn()
    {
        // Act.
        await using var session = Open();
        var baseline = session.Baseline();

        // Assert.
        // The .adp is one MIME line; everything drawn came from the folder around it.
        var elements = Assert.IsType<DiagramAddDelta>(Assert.Single(baseline)).Elements;
        Assert.Contains(elements, element => element.Id == "role:nginx");
        Assert.Contains(elements, element => element.Id == "playbook:site.yml");
    }

    [Fact]
    public void TheFactory_AnswersForTheAnsibleOrigin()
    {
        // Arrange, act and assert.
        Assert.Equal(Diagram.AnsibleStructure.Origin, _factory.Origin);
    }

    [Fact]
    public async Task TheBaseline_IsOneAddOfEverythingInView()
    {
        // Act.
        await using var session = Open();

        // Assert.
        Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline()));
    }

    // ---- the viewport -----------------------------------------------------------------------

    [Fact]
    public async Task NarrowingTheViewport_RemovesWhatFellOutOfIt()
    {
        // Arrange.
        await using var session = Open();
        session.Baseline();

        // Act.
        var deltas = session.UpdateView(new DiagramViewport(-10, -10, 10, 10));

        // Assert.
        Assert.Contains(deltas, delta => delta is DiagramRemoveDelta);
    }

    [Fact]
    public async Task WideningTheViewportAgain_BringsThingsBack()
    {
        // Arrange.
        await using var session = Open();
        session.Baseline();
        session.UpdateView(new DiagramViewport(-10, -10, 10, 10));

        // Act.
        var deltas = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        var added = Assert.IsType<DiagramAddDelta>(deltas.Last(delta => delta is DiagramAddDelta));
        Assert.Contains(added.Elements, element => element.Id == "role:postgres");
    }

    // ---- following the folder ----------------------------------------------------------------

    [Fact]
    public async Task AChangeOnDisk_ReachesTheSessionAsDeltas()
    {
        // Arrange.
        await using var session = Open();
        session.Baseline();
        var pushes = new List<IReadOnlyList<DiagramDelta>>();
        session.Changed += (_, args) =>
        {
            lock (pushes)
            {
                pushes.Add(args.Deltas);
            }
        };

        // Act.
        File.Delete(IoPath.Combine(_root, "dbservers.yml"));

        // Assert.
        var deltas = await WaitForPush(pushes);
        var removed = Assert.IsType<DiagramRemoveDelta>(deltas.First(delta => delta is DiagramRemoveDelta));
        Assert.Contains("playbook:dbservers.yml", removed.ElementIds);
    }

    [Fact]
    public async Task ADisposedSession_StopsHearingAboutTheFolder()
    {
        // Arrange.
        var session = Open();
        session.Baseline();
        var pushes = new List<IReadOnlyList<DiagramDelta>>();
        session.Changed += (_, args) =>
        {
            lock (pushes)
            {
                pushes.Add(args.Deltas);
            }
        };

        // Act.
        await session.DisposeAsync();
        File.Delete(IoPath.Combine(_root, "dbservers.yml"));
        await Task.Delay(SettleDelay + SettleDelay + SettleDelay, TestContext.Current.CancellationToken);

        // Assert.
        lock (pushes)
        {
            Assert.Empty(pushes);
        }
    }

    [Fact]
    public async Task TwoSessions_BothKeepHearing_WhenOneOfThemCloses()
    {
        // Arrange.
        // The failure the store's claim count exists to prevent: one connection closing must not
        // leave the other's diagram quietly stale.
        var first = Open();
        await using var second = Open();
        first.Baseline();
        second.Baseline();

        var pushes = new List<IReadOnlyList<DiagramDelta>>();
        second.Changed += (_, args) =>
        {
            lock (pushes)
            {
                pushes.Add(args.Deltas);
            }
        };

        // Act.
        await first.DisposeAsync();
        File.Delete(IoPath.Combine(_root, "dbservers.yml"));

        // Assert.
        await WaitForPush(pushes);
    }

    // ---- what it refuses -----------------------------------------------------------------------

    [Fact]
    public async Task MoveElement_IsRefusedWithAReason_NotSilentlyAccepted()
    {
        // Arrange.
        await using var session = Open();
        session.Baseline();

        // Act.
        var reason = await session.MoveElementAsync("role:nginx", "role:common", 0, TestContext.Current.CancellationToken);

        // Assert.
        // The seam returns a reason string so a type that cannot do a thing can say so in the
        // user's terms - rather than throwing, or worse, quietly reporting success.
        Assert.NotEqual("", reason);
        Assert.Contains("folder", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MoveElement_ChangesNothingOnDisk()
    {
        // Arrange.
        await using var session = Open();
        session.Baseline();
        var before = Snapshot();

        // Act.
        await session.MoveElementAsync("role:nginx", "role:common", 0, TestContext.Current.CancellationToken);

        // Assert.
        // A refusal that still touched the disk is the bug this guards.
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void TheSession_TakesNoHistoryStack()
    {
        // Arrange, act and assert.
        // Not "takes one and never uses it": a module with no commands has no reason to hold
        // the project's history, and the missing parameter is the statement.
        var parameters = typeof(AnsibleSessionFactory).GetConstructors().Single().GetParameters();
        Assert.DoesNotContain(parameters, parameter =>
            parameter.ParameterType.Name.Contains("History", StringComparison.OrdinalIgnoreCase));
    }

    // ---- plumbing --------------------------------------------------------------------------------

    private Dictionary<string, byte[]> Snapshot() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);

    private static async Task<IReadOnlyList<DiagramDelta>> WaitForPush(List<IReadOnlyList<DiagramDelta>> pushes)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < WaitLimit)
        {
            lock (pushes)
            {
                if (pushes.Count > 0)
                {
                    return pushes[^1];
                }
            }
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"No deltas arrived within {WaitLimit}.");
        return null!;
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
