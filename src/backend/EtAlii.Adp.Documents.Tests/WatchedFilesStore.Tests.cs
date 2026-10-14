using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The watched-folder store's contract when a snapshot is read from named files rather than a whole
/// folder - the shape of a solution, which names its projects (code-structure FR-037). The subject
/// here is a manifest whose lines name the other files it is read with.
/// </summary>
public sealed class WatchedFilesStoreTests : WatchedFolderStoreContract<WatchedFilesStoreTests.Reading>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    protected override WatchedFolderStore<Reading> CreateStore(TimeSpan settleDelay) => new ManifestStore(settleDelay);

    protected override void Populate(string root)
    {
        File.WriteAllText(Path.Combine(root, "manifest.txt"), "a.txt");
        File.WriteAllText(Path.Combine(root, "a.txt"), "first");
    }

    protected override string SubjectOf(string root) => Path.Combine(root, "manifest.txt");

    protected override Task ChangeAsync(string root, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "changed", cancellationToken);

    protected override bool ShowsTheChange(Reading snapshot) => snapshot.Files["a.txt"] == "changed";

    protected override Task TouchAsync(string root, int index, CancellationToken cancellationToken) =>
        File.AppendAllTextAsync(Path.Combine(root, "a.txt"), $" touch {index}", cancellationToken);

    [Fact]
    public async Task AFileTheSnapshotNeverRead_IsNotHeard_ThoughItsFolderIsWatched()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        Store.GetOrLoad(Subject);
        var heard = new List<Reading>();
        using var watch = Store.Watch(Subject, snapshot =>
        {
            lock (heard)
            {
                heard.Add(snapshot);
            }
        });

        // Act: written into the same folder as the named files, which is the point - the filter is
        // per file, not per directory.
        await File.WriteAllTextAsync(Path.Combine(Root, "build.log"), "not read", cancellationToken);
        await Task.Delay(SettleDelay * 4, cancellationToken);

        // Assert, with the liveness control: silence only means something if a named file is heard.
        lock (heard)
        {
            Assert.Empty(heard);
        }
        await ChangeAsync(Root, cancellationToken);
        await Until(() => heard.Count > 0, heard, cancellationToken);
    }

    [Fact]
    public async Task AFileNamedByTheLatestRead_IsWatchedFromThatReadOn()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        Store.GetOrLoad(Subject);
        var heard = new List<Reading>();
        using var watch = Store.Watch(Subject, snapshot =>
        {
            lock (heard)
            {
                heard.Add(snapshot);
            }
        });
        Directory.CreateDirectory(Path.Combine(Root, "deeper"));
        var added = Path.Combine("deeper", "b.txt");
        await File.WriteAllTextAsync(Path.Combine(Root, added), "below", cancellationToken);

        // Act: the manifest names a file in a folder nothing watched, as a project added to a solution
        // does; then that file changes.
        await File.WriteAllTextAsync(Subject, $"a.txt\n{added}", cancellationToken);
        await Until(() => heard.Count > 0 && heard[^1].Files.ContainsKey(added), heard, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Root, added), "changed below", cancellationToken);

        // Assert.
        await Until(() => heard[^1].Files[added] == "changed below", heard, cancellationToken);
    }

    [Fact]
    public async Task ADeletedNamedFile_IsHeard()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        Store.GetOrLoad(Subject);
        var heard = new List<Reading>();
        using var watch = Store.Watch(Subject, snapshot =>
        {
            lock (heard)
            {
                heard.Add(snapshot);
            }
        });

        // Act.
        File.Delete(Path.Combine(Root, "a.txt"));

        // Assert.
        await Until(() => heard.Count > 0 && !heard[^1].Files.ContainsKey("a.txt"), heard, cancellationToken);
    }

    private static async Task Until(Func<bool> condition, List<Reading> heard, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < Patience)
        {
            lock (heard)
            {
                if (condition())
                {
                    return;
                }
            }
            await Task.Delay(25, cancellationToken);
        }

        Assert.Fail($"Nothing the test waited for was heard within {Patience}.");
    }

    /// <summary>The manifest's text and the files it names that exist, by the name it gives them.</summary>
    public sealed record Reading(string Manifest, IReadOnlyDictionary<string, string> Files);

    /// <summary>Reads a manifest and the files its lines name; watches exactly those.</summary>
    private sealed class ManifestStore(TimeSpan settleDelay) : WatchedFolderStore<Reading>(settleDelay)
    {
        protected override Reading Read(string folder)
        {
            var manifest = SharedDocumentReader.ReadAllText(folder);
            var directory = Path.GetDirectoryName(folder)!;
            var files = Names(manifest)
                .Where(name => File.Exists(Path.Combine(directory, name)))
                .ToDictionary(name => name, name => SharedDocumentReader.ReadAllText(Path.Combine(directory, name)), StringComparer.Ordinal);
            return new Reading(manifest, files);
        }

        protected override IReadOnlyCollection<string> WatchedFiles(string folder, Reading snapshot)
        {
            var directory = Path.GetDirectoryName(folder)!;
            return [folder, .. Names(snapshot.Manifest).Select(name => Path.Combine(directory, name))];
        }

        private static IEnumerable<string> Names(string manifest) =>
            manifest.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
