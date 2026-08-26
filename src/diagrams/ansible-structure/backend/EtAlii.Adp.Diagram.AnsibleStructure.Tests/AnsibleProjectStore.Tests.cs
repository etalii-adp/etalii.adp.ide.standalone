using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The store: one project per folder, kept true as the tree changes (Requirement 3.5).
/// </summary>
/// <remarks>
/// Against a temp copy of the fixture rather than the fixture itself, because these tests
/// deliberately change files - and the committed trees are the one thing in this module nothing
/// is allowed to write to.
/// </remarks>
public class AnsibleProjectStoreTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private readonly string _root;
    private readonly AnsibleProjectStore _store = new(new AnsibleProjectReader(), SettleDelay);

    public AnsibleProjectStoreTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        CopyTree(IoPath.Combine("Fixtures", "infrastructure"), _root);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ---- holding one project per folder ----------------------------------------------------

    [Fact]
    public void GetOrLoad_ReadsOnce_AndHandsTheSameProjectBack()
    {
        // Act.
        var first = _store.GetOrLoad(_root);
        var second = _store.GetOrLoad(_root);

        // Assert.
        // The same instance, not merely an equal one: one project per folder is what stops two
        // connections seeing different structures.
        Assert.Same(first, second);
    }

    [Fact]
    public void Get_DoesNotLoad()
    {
        // Act and assert, step by step.
        Assert.Null(_store.Get(_root));
        _store.GetOrLoad(_root);
        Assert.NotNull(_store.Get(_root));
    }

    [Fact]
    public void TheStore_HasNoWayToWriteAnything()
    {
        // Arrange, act and assert.
        // A store for a read-only type has one job. This is a compile-time claim made at
        // runtime: if a save method is ever added, this test says so out loud.
        var methods = typeof(IAnsibleProjectStore).GetMethods().Select(method => method.Name).ToArray();
        Assert.DoesNotContain(methods, name =>
            name.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Write", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Update", StringComparison.OrdinalIgnoreCase));
    }

    // ---- following the tree ------------------------------------------------------------------

    [Fact]
    public async Task AChangedFile_IsAnnouncedWithTheRereadProject()
    {
        // Arrange.
        _store.GetOrLoad(_root);
        var changes = Watch();

        // Act.
        // nginx gains a second dependency. (common is deliberately the role with no meta at
        // all, so it is the wrong one to edit here.)
        File.WriteAllText(
            IoPath.Combine(_root, "roles", "nginx", "meta", "main.yml"),
            "---\ndependencies:\n  - role: common\n  - role: postgres\n");

        // Assert.
        var project = await WaitForChange(changes);
        Assert.Equal(
            ["common", "postgres"],
            project.Roles.Single(role => role.Name == "nginx").Dependencies.Select(d => d.Target));
    }

    [Fact]
    public async Task ANewRoleFolder_IsPickedUp()
    {
        // Arrange.
        _store.GetOrLoad(_root);
        var changes = Watch();

        // Act.
        var added = IoPath.Combine(_root, "roles", "redis", "tasks");
        Directory.CreateDirectory(added);
        File.WriteAllText(IoPath.Combine(added, "main.yml"), "---\n- name: Install redis\n  ansible.builtin.package:\n    name: redis\n");

        // Assert.
        var project = await WaitForChange(changes);
        Assert.Contains(project.Roles, role => role.Name == "redis");
    }

    [Fact]
    public async Task ADeletedFile_TakesItsNodeWithIt()
    {
        // Arrange.
        _store.GetOrLoad(_root);
        var changes = Watch();

        // Act.
        File.Delete(IoPath.Combine(_root, "dbservers.yml"));

        // Assert.
        var project = await WaitForChange(changes);
        Assert.DoesNotContain(project.Playbooks, playbook => playbook.Name == "dbservers.yml");
    }

    [Fact]
    public async Task ABurstOfChanges_CostsFarFewerRereadsThanChanges()
    {
        // Arrange.
        const int changeCount = 6;
        _store.GetOrLoad(_root);
        var changes = Watch();

        // Act.
        // Written back to back, as a multi-file save actually arrives. An earlier version of
        // this test spaced them 20ms apart and asserted exactly one re-read; that failed
        // intermittently, because any stall longer than the settle delay lets the timer fire
        // mid-burst and produce a second - so it was measuring the machine's load, not the
        // store's coalescing.
        for (var i = 0; i < changeCount; i++)
        {
            File.AppendAllText(IoPath.Combine(_root, "webservers.yml"), $"# touch {i}\n");
        }

        // Assert.
        await WaitForChange(changes);
        await Task.Delay(SettleDelay + SettleDelay, TestContext.Current.CancellationToken);
        lock (changes)
        {
            // The claim that is actually true and worth guarding: a burst costs materially
            // fewer re-reads than it has changes. Demanding exactly one would be a clock test.
            Assert.InRange(changes.Count, 1, changeCount - 1);
        }
    }

    [Fact]
    public async Task TheStoreHandsOutTheRereadProject_NotTheStaleOne()
    {
        // Arrange.
        _store.GetOrLoad(_root);
        var changes = Watch();

        // Act.
        File.Delete(IoPath.Combine(_root, "dbservers.yml"));
        await WaitForChange(changes);

        // Assert.
        // Re-read before announcing, so a listener that asks the store during the event is not
        // handed the project the event exists to replace.
        Assert.DoesNotContain(_store.Get(_root)!.Playbooks, playbook => playbook.Name == "dbservers.yml");
    }

    // ---- lifetime ----------------------------------------------------------------------------

    [Fact]
    public void ReleasingOneOfTwoClaims_KeepsTheFolderWatched()
    {
        // Arrange.
        // The failure this split exists to prevent: a second connection closing must not tear
        // the watcher out from under the first, whose diagram would then go quietly stale.
        _store.Acquire(_root);
        _store.Acquire(_root);

        // Act.
        _store.Release(_root);

        // Assert.
        Assert.NotNull(_store.Get(_root));
    }

    [Fact]
    public void ReleasingTheLastClaim_DropsTheFolder()
    {
        // Arrange.
        _store.Acquire(_root);

        // Act.
        _store.Release(_root);

        // Assert.
        Assert.Null(_store.Get(_root));
    }

    [Fact]
    public async Task AReleasedFolder_IsReadAfreshOnTheNextAsk()
    {
        // Arrange.
        var before = _store.Acquire(_root);
        _store.Release(_root);

        // Act.
        File.Delete(IoPath.Combine(_root, "dbservers.yml"));
        await Task.Delay(SettleDelay, TestContext.Current.CancellationToken);
        var after = _store.GetOrLoad(_root);

        // Assert.
        Assert.NotSame(before, after);
        Assert.DoesNotContain(after.Playbooks, playbook => playbook.Name == "dbservers.yml");
    }

    [Fact]
    public void ReleasingAFolderNobodyHeld_IsNotAnError()
    {
        // Act and assert.
        _store.Release(_root);
        _store.Release(IoPath.Combine(_root, "never-loaded"));
    }

    // ---- plumbing ------------------------------------------------------------------------------

    private List<AnsibleProject> Watch()
    {
        var changes = new List<AnsibleProject>();
        _store.Changed += (_, args) =>
        {
            lock (changes)
            {
                changes.Add(args.Project);
            }
        };
        return changes;
    }

    private static async Task<AnsibleProject> WaitForChange(List<AnsibleProject> changes)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < WaitLimit)
        {
            lock (changes)
            {
                if (changes.Count > 0)
                {
                    return changes[^1];
                }
            }
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"The folder was not re-read within {WaitLimit}.");
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
