using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly ServiceProvider _provider;

    public AnsibleSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        CopyTree(IoPath.Combine("Fixtures", "infrastructure"), _root);
        // The registration a real Add would have written. Nothing else is added to the folder.
        File.WriteAllText(IoPath.Combine(_root, "infrastructure.adp"), "ansible/structure\n");
        _provider = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddAnsibleStructure().BuildServiceProvider();
        _factory = new AnsibleSessionFactory(
            _store, new AnsibleElementMapper(), _provider.GetRequiredService<IHistoryStackStore>());
    }

    public void Dispose()
    {
        _provider.Dispose();
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
    public void TheSession_TakesTheHistoryStack_ForItsOneEdit()
    {
        // Arrange, act and assert.
        // This assertion used to read DoesNotContain, and said so in the module's own terms: a
        // type with no commands had no reason to hold the project's history. ansible-refinements
        // gave it exactly one edit - a reposition, dispatched as core's
        // SetRegistrationLayoutCommand - so the parameter is now the statement instead of its
        // absence. The module still owns no command of its own, which
        // AddAnsibleStructureTests keeps asserting.
        var parameters = typeof(AnsibleSessionFactory).GetConstructors().Single().GetParameters();
        Assert.Contains(parameters, parameter =>
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


    // ---- the one edit: a reposition (Requirements 1.1, 1.3, 1.5, 2.1, 2.3) -------------------

    [Fact]
    public async Task MoveElementTo_WritesTheAuthoredPositionIntoTheRegistration()
    {
        // Arrange.
        var registration = IoPath.Combine(_root, "infrastructure.adp");
        await using var session = Open();
        session.Baseline();

        // Act.
        var answer = await session.MoveElementToAsync("role:common", 321, 123, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(string.Empty, answer);
        var stored = RegistrationLayout.Read(registration);
        Assert.Equal(new RegistrationPosition(321, 123), stored["role:common"]);
    }

    [Fact]
    public async Task MoveElementTo_IsUndoneBackToTheRegistrationsPreviousBytes()
    {
        // Arrange: the whole point of dispatching a command rather than writing directly.
        var registration = IoPath.Combine(_root, "infrastructure.adp");
        var before = await File.ReadAllBytesAsync(registration, TestContext.Current.CancellationToken);
        await using var session = Open();
        session.Baseline();
        await session.MoveElementToAsync("role:common", 321, 123, TestContext.Current.CancellationToken);

        // Act.
        await _provider.GetRequiredService<IHistoryStackStore>().Get(_root)
            .UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(before, await File.ReadAllBytesAsync(registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveElementTo_RefusesAnEdge_BecauseAnEdgeFollowsItsEndpoints()
    {
        // Arrange.
        await using var session = Open();
        session.Baseline();

        // Act.
        var reason = await session.MoveElementToAsync(
            "edge:playbook:webservers.yml|Role|nginx", 321, 123, TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotEqual(string.Empty, reason);
        Assert.Empty(RegistrationLayout.Read(IoPath.Combine(_root, "infrastructure.adp")));
    }

    [Fact]
    public async Task MoveElementTo_RefusesWithoutAHistory_BecauseThatDiagramIsReadOnly()
    {
        // Arrange: the null-stack case, which is how a read-only diagram presents itself.
        await using var session = new AnsibleSession(
            _root, _store, new AnsibleElementMapper(), IoPath.Combine(_root, "infrastructure.adp"));
        session.Baseline();

        // Act.
        var reason = await session.MoveElementToAsync("role:common", 321, 123, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("This diagram is read-only.", reason);
        Assert.Empty(RegistrationLayout.Read(IoPath.Combine(_root, "infrastructure.adp")));
    }

    [Fact]
    public async Task MoveElementTo_RefusesWithoutARegistration_BecauseThereIsNowhereToStoreIt()
    {
        // Arrange.
        await using var session = new AnsibleSession(
            _root, _store, new AnsibleElementMapper(), null, _provider.GetRequiredService<IHistoryStackStore>().Get(_root));
        session.Baseline();

        // Act.
        var reason = await session.MoveElementToAsync("role:common", 321, 123, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("nowhere to store", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAuthoredPosition_IsWhereTheElementIsDeliveredOnTheNextOpen()
    {
        // Arrange: the reopen half of Requirement 2.4 - the overlay outlives the session.
        await using (var first = Open())
        {
            first.Baseline();
            await first.MoveElementToAsync("role:common", 321, 123, TestContext.Current.CancellationToken);
        }

        // Act.
        await using var second = Open();
        var baseline = second.Baseline();

        // Assert.
        var elements = Assert.IsType<DiagramAddDelta>(Assert.Single(baseline)).Elements;
        var moved = elements.Single(element => element.Id == "role:common");
        Assert.Equal(321, moved.X);
        Assert.Equal(123, moved.Y);
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
