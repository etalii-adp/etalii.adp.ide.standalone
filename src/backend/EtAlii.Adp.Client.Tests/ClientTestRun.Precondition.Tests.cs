using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Client.Tests;

/// <summary>
/// What the client suite's precondition must and must not accept.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both fixtures are BUILT, and none is borrowed from a real tree.</b> That is not a style
/// preference. The directory this check looks at is created by a client test run - it is Vite's
/// cache - so <b>no tree that has ever run the client suite is a specimen of the never-run
/// case</b>. Borrowing was never available; it only looked available. Measured the hard way: a
/// worktree offered as a clean specimen turned out to hold the folder with a timestamp matching the
/// reporting session's own `npm test` log to the second.
/// </para>
/// <para>
/// <b>The defect being closed was wrong in both directions at once</b>, which is worse than a weak
/// check because its result carried no information either way. `src/package.json` declares
/// <c>workspaces: ["client", "diagrams/*/client"]</c>, so an install hoists everything to
/// <c>src/node_modules</c> and creates NO <c>src/client/node_modules</c> - the check therefore
/// refused a correctly installed tree. And <c>Directory.Exists</c> is satisfied by a directory
/// holding only <c>.vite</c>, so it accepted a tree with no install at all. Self-perpetuating: the
/// first run in a fresh tree failed, and every run afterwards passed on the cache the previous run
/// had left.
/// </para>
/// <para>
/// <b>The marker is <c>node_modules/.package-lock.json</c> at the workspace root, and it is chosen
/// BECAUSE nothing but npm writes it</b> rather than because it happens to be there today. npm
/// maintains it as its own hidden lockfile of the installed tree. A folder-existence test that
/// happens to pick a path nothing else writes is luck; one that picks a path because nothing else
/// writes it survives somebody adding a tool next year that caches under <c>node_modules</c> - and
/// that is not hypothetical, because <c>.vite-temp</c> already appears in the ROOT
/// <c>node_modules</c> as well, so even the root folder's existence is not npm's to vouch for.
/// </para>
/// </remarks>
public class ClientTestRunPreconditionTests : IDisposable
{
    private readonly string _root = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-client-precondition-" + Guid.NewGuid().ToString("N"));

    public ClientTestRunPreconditionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        TestFolder.TryDelete(_root);

        GC.SuppressFinalize(this);
    }

    /// <summary>A workspace layout under the fixture: returns the client folder, as the check takes.</summary>
    /// <param name="rootInstall">What npm leaves at the workspace root: the tree and its hidden lockfile.</param>
    /// <param name="rootCache">
    /// What a client test run leaves at the workspace ROOT and npm does not - `.vite-temp`. This is
    /// the parameter that makes the accepting direction reproducible, and it is not invented: that
    /// entry is present in the root `node_modules` of installed trees on this machine, so a
    /// directory-existence test on the root is satisfiable without any install at all.
    /// </param>
    /// <param name="clientCache">What a client test run leaves beside the package: Vite's cache.</param>
    private string Workspace(bool rootInstall, bool rootCache = false, bool clientCache = false)
    {
        var workspace = IoPath.Combine(_root, "src");
        var client = IoPath.Combine(workspace, "client");
        Directory.CreateDirectory(client);

        if (rootInstall)
        {
            Directory.CreateDirectory(IoPath.Combine(workspace, "node_modules", ".bin"));
            File.WriteAllText(IoPath.Combine(workspace, "node_modules", ".package-lock.json"), "{}");
        }

        if (rootCache)
        {
            Directory.CreateDirectory(IoPath.Combine(workspace, "node_modules", ".vite-temp"));
        }

        if (clientCache)
        {
            Directory.CreateDirectory(IoPath.Combine(client, "node_modules", ".vite"));
        }

        return client;
    }

    [Fact]
    public void AWorkspaceInstall_IsAccepted_EvenWithNoFolderBesideThePackage()
    {
        // Arrange: exactly what `npm install` in src/ produces - a hoisted root install and no
        // per-package folder at all. This is every correctly installed tree.
        var client = Workspace(rootInstall: true);

        // Act.
        var missing = ClientTestRun.MissingDependencies(client);

        // Assert: nothing missing. Refusing here is refusing a tree that can run the suite
        // perfectly well, and the message it prints tells the reader to run an install that has
        // already been run and cannot help.
        Assert.Empty(missing);
    }

    [Fact]
    public void CachesWithoutAnInstall_AreRefused_ThoughBothFoldersExist()
    {
        // Arrange: NO install anywhere, and both `node_modules` folders present because test runs
        // made them - `.vite-temp` at the root, `.vite` beside the package. Every folder the old
        // check looked for exists, and nothing is installed.
        //
        // THIS IS THE ACCEPTING DIRECTION, and my first attempt at it did not reproduce: I built
        // the fixture with no root folder at all, so the old check still refused and the test
        // passed against the defect. A guard that passes against the defect is a companion wearing
        // a detector's name, and the only reason this one is not is that the root cache entry is a
        // real thing rather than a convenient one.
        var client = Workspace(rootInstall: false, rootCache: true, clientCache: true);

        // Act.
        var missing = ClientTestRun.MissingDependencies(client);

        // Assert: refused. Accepting this is what the check exists to prevent - the run proceeds
        // and fails with a module-resolution stack instead of a sentence.
        Assert.NotEmpty(missing);
    }

    [Fact]
    public void AnEmptyTree_IsRefused()
    {
        // The floor, and a companion: with neither an install nor a cache there is nothing to
        // argue about. It held before this change too. Kept because a check that somehow passed
        // here would be reporting on nothing at all.
        Assert.NotEmpty(ClientTestRun.MissingDependencies(Workspace(rootInstall: false)));
    }

    [Fact]
    public void AnInstalledTreeThatHasAlsoRunTheSuite_IsAccepted()
    {
        // Both present, which is what every tree on this board looks like after one gate. A
        // companion: it passed before this change as well - for the cache rather than for the
        // install - and that is precisely why the board's greens were uninformative rather than
        // wrong. Kept because it is the state everything is actually in.
        Assert.Empty(ClientTestRun.MissingDependencies(Workspace(rootInstall: true, rootCache: true, clientCache: true)));
    }
}
