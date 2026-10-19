using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The shared store publishes a save through the file writer it is handed, and waits for that
/// writer without blocking the caller's thread (code-structure FR-068).
/// </summary>
public sealed class LifecycleDocumentStoreWriterTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-lifecycle-writer-" + Guid.NewGuid().ToString("N"));

    public LifecycleDocumentStoreWriterTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestFolder.TryDelete(_folder);

    [Fact]
    public async Task ASave_WaitsForItsWriter_WithoutBlocking_ThenTellsTheSessions()
    {
        // Arrange: a writer that holds every save until it is let go, as one waiting for another save
        // of the same path does.
        var path = IoPath.Combine(_folder, "notes.txt");
        await File.WriteAllTextAsync(path, "before\n", TestContext.Current.CancellationToken);
        var writer = new HeldWriter();
        var store = new LineStore(writer);
        var told = new List<string>();
        store.Changed += (_, args) => told.Add(args.Path);
        var edited = LineDocument.Parse((await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document.Text + "after\n");

        // Act: called on this thread, the save returns while its writer still holds it.
        var saving = store.SaveAsync(path, edited, TestContext.Current.CancellationToken);

        // Assert: the writer was handed the text, nobody was told yet, and both follow its release.
        Assert.False(saving.IsCompleted, "The save completed while its writer still held it.");
        var held = Assert.Single(writer.Saves);
        Assert.Equal((path, "before\nafter\n"), held);
        Assert.Empty(told);
        writer.Release();
        var saved = await saving;
        Assert.False(saved.Failed, saved.Error);
        Assert.Equal([path], told);
    }

    /// <summary>A store over plain lines.</summary>
    private sealed class LineStore(IFileWriter writer) : LifecycleDocumentStore<LineDocument, string>(writer)
    {
        protected override LineDocument Read(string text) => LineDocument.Parse(text);

        protected override string TextOf(LineDocument body) => body.Text;

        protected override ParseOutcome<string> Parse(LineDocument body) => new(body.Text, []);
    }

    /// <summary>Records each save and completes none of them until <see cref="Release"/>; writes nothing.</summary>
    private sealed class HeldWriter : IFileWriter
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(string Path, string Content)> Saves { get; } = [];

        public void Release() => _released.SetResult();

        public Task SaveAsync(string path, string content, CancellationToken cancellationToken)
        {
            Saves.Add((path, content));
            return _released.Task;
        }

        public Task SaveAsync(string path, byte[] content, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A store saves text.");

        public Task DeleteAsync(string path, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A store's save deletes nothing.");
    }
}
