using EtAlii.Adp.Documents;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The store: one project per folder, kept true as the tree changes (Requirement 3.5), through the
/// shared watched-folder contract and the module's own cases.
/// </summary>
/// <remarks>
/// Against a temp copy of the fixture rather than the fixture itself, because these tests
/// deliberately change files - and the committed trees are the one thing in this module nothing
/// is allowed to write to.
/// </remarks>
public sealed class AnsibleProjectStoreTests : WatchedFolderStoreContract<AnsibleProject>
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    protected override WatchedFolderStore<AnsibleProject> CreateStore(TimeSpan settleDelay) =>
        new AnsibleProjectStore(new AnsibleProjectReader(), settleDelay);

    protected override void Populate(string root) => TestFolder.CopyTree(IoPath.Combine("Fixtures", "infrastructure"), root);

    /// <summary>nginx gains a second dependency. (common is deliberately the role with no meta at all, so it is the wrong one to edit here.)</summary>
    protected override Task ChangeAsync(string root, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            IoPath.Combine(root, "roles", "nginx", "meta", "main.yml"),
            "---\ndependencies:\n  - role: common\n  - role: postgres\n",
            cancellationToken);

    protected override bool ShowsTheChange(AnsibleProject snapshot) =>
        snapshot.Roles.Single(role => role.Name == "nginx").Dependencies.Select(d => d.Target)
            .SequenceEqual(["common", "postgres"]);

    protected override Task TouchAsync(string root, int index, CancellationToken cancellationToken) =>
        File.AppendAllTextAsync(IoPath.Combine(root, "webservers.yml"), $"# touch {index}\n", cancellationToken);

    [Fact]
    public void TheStore_HasNoWayToWriteAnything()
    {
        // Arrange, act and assert.
        // A store for a read-only type has one job. This is a compile-time claim made at
        // runtime: if a save method is ever added, this test says so out loud.
        var methods = typeof(IAnsibleProjectStore).GetInterfaces().Append(typeof(IAnsibleProjectStore))
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
        var heard = Heard();

        // Act.
        var added = IoPath.Combine(Root, "roles", "redis", "tasks");
        Directory.CreateDirectory(added);
        await File.WriteAllTextAsync(IoPath.Combine(added, "main.yml"), "---\n- name: Install redis\n  ansible.builtin.package:\n    name: redis\n", TestContext.Current.CancellationToken);

        // Assert.
        var project = await heard.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Contains(project.Roles, role => role.Name == "redis");
    }

    [Fact]
    public async Task ADeletedFile_TakesItsNodeWithIt()
    {
        // Arrange.
        var heard = Heard();

        // Act.
        File.Delete(IoPath.Combine(Root, "dbservers.yml"));

        // Assert.
        var project = await heard.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(project.Playbooks, playbook => playbook.Name == "dbservers.yml");
    }

    /// <summary>The first project the store announces for the folder after this call.</summary>
    private TaskCompletionSource<AnsibleProject> Heard()
    {
        Store.GetOrLoad(Root);
        var heard = new TaskCompletionSource<AnsibleProject>(TaskCreationOptions.RunContinuationsAsynchronously);
        Store.Changed += (_, args) => heard.TrySetResult(args.Snapshot);
        return heard;
    }
}
