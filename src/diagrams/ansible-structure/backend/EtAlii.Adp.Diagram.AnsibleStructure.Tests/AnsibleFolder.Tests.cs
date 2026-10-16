using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Microsoft.Extensions.Time.Testing;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The open folders: one reading per folder, kept true as the tree changes (Requirement 3.5) through the
/// library's folder body, and kept open while a session claims it (ansible-structure-disl-fbl, task 10).
/// These are the store's tests, moved: what the shared watched-folder contract asked of the store is asked
/// here of <see cref="AnsibleFolders"/>, with the module's own cases after it.
/// </summary>
/// <remarks>
/// Against a temp copy of the fixture rather than the fixture itself, because these tests deliberately
/// change files - and the committed trees are the one thing in this module nothing is allowed to write to.
/// </remarks>
public sealed class AnsibleFolderTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(AnsibleDefinition.Binding.Body.Settle);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    /// <summary>How long a test listens, in real time, for something that must not be heard.</summary>
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(400);

    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.AnsibleFolder");
    private readonly FakeTimeProvider _clock = new();
    private readonly AnsibleFolders _folders;
    private readonly string _root;

    public AnsibleFolderTests()
    {
        _folders = new AnsibleFolders(_clock);
        _root = Fill("root");
    }

    public void Dispose()
    {
        _folders.Dispose();
        _folder.Dispose();
    }

    // ---- one reading per folder --------------------------------------------------------------

    [Fact]
    public void GetOrLoad_ReadsOnce_AndHandsTheSameFolderBack()
    {
        // Act.
        var first = _folders.GetOrLoad(_root);
        var second = _folders.GetOrLoad(_root);

        // Assert: the same instance and the same model, not merely equal ones - one reading per folder is
        // what stops two connections seeing different things.
        Assert.Same(first, second);
        Assert.Same(first.Disl, second.Disl);
    }

    [Fact]
    public async Task AChange_IsAnnouncedWithTheRereadModel_AndTheFoldersHandThatOneOut()
    {
        // Arrange.
        Assert.False(ShowsTheChange(_folders.GetOrLoad(_root).Disl));
        var heard = Heard(_root);

        // Act.
        await ChangeAsync(_root, TestContext.Current.CancellationToken);

        // Assert: read again before announcing, so a listener that asks during the event is not handed the
        // model the event exists to replace.
        var model = await WaitFor(heard, ShowsTheChange);
        Assert.Same(model, _folders.GetOrLoad(_root).Disl);
    }

    [Fact]
    public async Task ABurstOfChanges_CostsFarFewerReadsThanChanges()
    {
        // Arrange.
        const int changeCount = 6;
        var reads = Reads(_folders.GetOrLoad(_root));

        // Act: written back to back, as a multi-file save arrives. Any stall longer than the settle delay
        // lets the timer fire mid-burst, so exactly one read would be a clock test.
        for (var i = 0; i < changeCount; i++)
        {
            await TouchAsync(_root, i, TestContext.Current.CancellationToken);
        }

        // Assert: the clock moves a settle delay only every settle delay of real time, as it would run by
        // itself, so events the file system is slow to report still fall into one window.
        await WaitFor(reads, 1, SettleDelay);
        Assert.False(
            await Eventually.TrueAsync(() => Settled(() => Count(reads) >= changeCount), TestContext.Current.CancellationToken, Silence, SettleDelay),
            "Every change of the burst cost a read of its own.");
    }

    // ---- claims ------------------------------------------------------------------------------

    [Fact]
    public void ReleasingOneOfTwoClaims_KeepsTheFolderOpen()
    {
        // Arrange: a second connection closing must not close the folder under the first, whose diagram
        // would then go quietly stale.
        var held = _folders.Acquire(_root);
        _folders.Acquire(_root);

        // Act.
        _folders.Release(_root);

        // Assert.
        Assert.Same(held, _folders.GetOrLoad(_root));
    }

    [Fact]
    public void ReleasingTheLastClaim_ClosesTheFolder()
    {
        // Arrange.
        var held = _folders.Acquire(_root);

        // Act.
        _folders.Release(_root);

        // Assert.
        Assert.NotSame(held, _folders.GetOrLoad(_root));
    }

    [Fact]
    public async Task AReleasedFolder_IsReadAfreshOnTheNextAsk()
    {
        // Arrange.
        var before = _folders.Acquire(_root);
        _folders.Release(_root);

        // Act: changed while nobody watches.
        await ChangeAsync(_root, TestContext.Current.CancellationToken);
        var after = _folders.GetOrLoad(_root);

        // Assert.
        Assert.NotSame(before, after);
        Assert.True(ShowsTheChange(after.Disl));
    }

    [Fact]
    public void ReleasingAFolderNobodyHeld_IsNotAnError()
    {
        // Act and assert.
        _folders.Release(_root);
        _folders.Release(IoPath.Combine(_root, "never-loaded"));
    }

    [Fact]
    public void AFolderOpenedWithItsRegistration_IsNotTheFolderOpenedWithout()
    {
        // Arrange: a folder is keyed by the registration that holds its positions, so the folder body
        // watches the file each session writes to.
        var registration = IoPath.Combine(_root, "infrastructure.adp");
        File.WriteAllText(registration, "ansible/structure\n");

        // Act.
        var with = _folders.GetOrLoad(_root, registration);
        var without = _folders.GetOrLoad(_root);

        // Assert.
        Assert.NotSame(with, without);
        Assert.Same(with, _folders.GetOrLoad(_root, registration));
        Assert.Equal(IoPath.GetFullPath(registration), with.Registration);
    }

    // ---- watches -----------------------------------------------------------------------------

    [Fact]
    public async Task AWatch_HearsItsFolder_AndNothingOnceItIsLetGo()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var opened = _folders.GetOrLoad(_root);
        var heard = new List<DislModel>();
        var watch = _folders.Watch(_root, null, folder =>
        {
            lock (heard)
            {
                heard.Add(folder.Disl);
            }
        });
        var reads = Reads(opened);

        // Act: heard while watched, then let go - twice, which is harmless - and changed again.
        await ChangeAsync(_root, cancellationToken);
        await WaitFor(heard, ShowsTheChange);
        watch.Dispose();
        watch.Dispose();
        int heardBefore;
        lock (heard)
        {
            heardBefore = heard.Count;
        }
        int readBefore;
        lock (reads)
        {
            readBefore = reads.Count;
        }
        await TouchAsync(_root, 0, cancellationToken);
        await WaitFor(reads, readBefore + 1);

        // Assert: the change was heard, and nothing after the watch was let go.
        lock (heard)
        {
            Assert.Contains(heard, ShowsTheChange);
            Assert.Equal(heardBefore, heard.Count);
        }
    }

    [Fact]
    public async Task AWatch_DoesNotHearAnotherFolder()
    {
        // Arrange.
        var other = Fill("other");
        _folders.GetOrLoad(_root);
        var reads = Reads(_folders.GetOrLoad(other));
        var heard = new int[1];
        using var watch = _folders.Watch(_root, null, _ => Interlocked.Increment(ref heard[0]));

        // Act.
        await ChangeAsync(other, TestContext.Current.CancellationToken);
        await WaitFor(reads, 1);

        // Assert.
        Assert.Equal(0, Volatile.Read(ref heard[0]));
    }

    [Fact]
    public async Task AnOverflow_IsReadAgain_AsThoughAFileHadChanged()
    {
        // Arrange: the one signal a watcher gives when it dropped events is Error, and which file changed
        // is then unknowable, so the folder must be read again (backend-centralization R2.9).
        var cancellationToken = TestContext.Current.CancellationToken;
        var opened = _folders.GetOrLoad(_root);
        var heard = Heard(_root);

        // Act: lose the change - nothing is watching while it happens, so no event can carry it - then
        // overflow, as the watcher itself would.
        var watcher = opened.FolderWatcher;
        Assert.NotNull(watcher);
        watcher.EnableRaisingEvents = false;
        await ChangeAsync(_root, cancellationToken);
        watcher.RaiseOverflow();

        // Assert.
        var model = await WaitFor(heard, _ => true);
        Assert.True(ShowsTheChange(model), "An overflow left the model looking fresh.");
    }

    // ---- the module's own cases ----------------------------------------------------------------

    [Fact]
    public void TheOpenFolders_HaveNoWayToWriteAnything()
    {
        // Arrange, act and assert.
        // A read-only tool type's folders have one job. This is a compile-time claim made at runtime: if a
        // save method is ever added, this test says so out loud.
        var methods = new[] { typeof(AnsibleFolders), typeof(AnsibleFolder) }
            .SelectMany(type => type.GetMethods())
            .Select(method => method.Name)
            .ToArray();
        Assert.DoesNotContain(methods, name =>
            name.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Write", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Update", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ANewRoleFolder_IsPickedUp()
    {
        // Arrange.
        var heard = Heard(_root);

        // Act.
        var added = IoPath.Combine(_root, "roles", "redis", "tasks");
        Directory.CreateDirectory(added);
        await File.WriteAllTextAsync(IoPath.Combine(added, "main.yml"), "---\n- name: Install redis\n  ansible.builtin.package:\n    name: redis\n", TestContext.Current.CancellationToken);

        // Assert.
        await WaitFor(heard, model => model.Diagram.Nodes.Any(node => node.Id == "role:redis"));
    }

    [Fact]
    public async Task ADeletedFile_TakesItsNodeWithIt()
    {
        // Arrange.
        var heard = Heard(_root);

        // Act.
        File.Delete(IoPath.Combine(_root, "dbservers.yml"));

        // Assert.
        await WaitFor(heard, model => model.Diagram.Nodes.All(node => node.Id != "playbook:dbservers.yml"));
    }

    [Fact]
    public async Task AFileAddedToARolesTemplates_ReadsTheFolderAgain()
    {
        // Arrange: no element is drawn from a template, but the plugin watches every folder under roles/,
        // so the folder is read again as the store read it.
        var reads = Reads(_folders.GetOrLoad(_root));

        // Act.
        await File.WriteAllTextAsync(
            IoPath.Combine(_root, "roles", "nginx", "templates", "site.conf.j2"), "server {}\n", TestContext.Current.CancellationToken);

        // Assert.
        await WaitFor(reads, 1);
    }

    // ---- plumbing ------------------------------------------------------------------------------

    /// <summary>A fresh copy of the fixture, named <paramref name="name"/>.</summary>
    private string Fill(string name)
    {
        var root = _folder.Combine(name);
        TestFolder.CopyTree(IoPath.Combine("Fixtures", "infrastructure"), root);
        return root;
    }

    /// <summary>nginx gains a second dependency. (common is deliberately the role with no meta at all, so it is the wrong one to edit here.)</summary>
    private static Task ChangeAsync(string root, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            IoPath.Combine(root, "roles", "nginx", "meta", "main.yml"),
            "---\ndependencies:\n  - role: common\n  - role: postgres\n",
            cancellationToken);

    private static bool ShowsTheChange(DislModel model) =>
        model.Diagram.Relations.Any(relation => relation.Source?.Id == "role:nginx" && relation.Target?.Id == "role:postgres");

    private static Task TouchAsync(string root, int index, CancellationToken cancellationToken) =>
        File.AppendAllTextAsync(IoPath.Combine(root, "webservers.yml"), $"# touch {index}\n", cancellationToken);

    /// <summary>Every model the folders announce for <paramref name="root"/> from now on, opening it first.</summary>
    private List<DislModel> Heard(string root)
    {
        _folders.GetOrLoad(root);
        var heard = new List<DislModel>();
        // Disposed with the folders.
        _ = _folders.Watch(root, null, folder =>
        {
            lock (heard)
            {
                heard.Add(folder.Disl);
            }
        });
        return heard;
    }

    /// <summary>Every change of <paramref name="opened"/> from now on that read the folder again.</summary>
    private static List<FolderBodyChange> Reads(AnsibleFolder opened)
    {
        var reads = new List<FolderBodyChange>();
        opened.Changed += (_, change) =>
        {
            if (!change.FolderRead) return;
            lock (reads)
            {
                reads.Add(change);
            }
        };
        return reads;
    }

    /// <summary>
    /// The first announced model that <paramref name="shows"/> holds of. Not merely the first announced: a
    /// write lands as a truncation and then its content, and a runner that stalls between the two for longer
    /// than the settle delay announces the empty file first, correctly.
    /// </summary>
    private async Task<DislModel> WaitFor(List<DislModel> heard, Func<DislModel, bool> shows)
    {
        var model = await Eventually.ValueAsync(
            () => Settled(() =>
            {
                lock (heard)
                {
                    return heard.FirstOrDefault(shows);
                }
            }),
            TestContext.Current.CancellationToken,
            WaitLimit);
        Assert.True(model is not null, $"The change was not announced within {WaitLimit}.");
        return model;
    }

    private async Task WaitFor(List<FolderBodyChange> reads, int count, TimeSpan? interval = null) =>
        Assert.True(
            await Eventually.TrueAsync(() => Settled(() => Count(reads) >= count), TestContext.Current.CancellationToken, WaitLimit, interval),
            $"The folder was not read again within {WaitLimit}.");

    private static int Count(List<FolderBodyChange> reads)
    {
        lock (reads)
        {
            return reads.Count;
        }
    }

    /// <summary><paramref name="probe"/>, asked once the folders' clock has moved a settle delay.</summary>
    private T Settled<T>(Func<T> probe)
    {
        _clock.Advance(SettleDelay);
        return probe();
    }
}
