using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// A read refused for a moment must not cost the change it was reading.
/// </summary>
/// <remarks>
/// c4's EditorResolution 60-second flake, in the shape this store shares: a .cld saved, the
/// watcher's reload refused by another holder of the file, the last good diagram kept - and no later
/// event to re-read it, so the change never arrived. The store now retries a refused read as
/// backend-centralization's DocumentLifecycle does. The refusal is scripted into the read rather than
/// raced for with a handle, so each test refuses exactly as often as it says, waits for nothing, and
/// asserts its refusal was actually consumed - a test that could pass by reading the file some other
/// way would prove nothing.
/// </remarks>
public class CausalLoopDocumentStoreRefusedReadTests : IDisposable
{
    private const string Text =
        "causal-loop 1\r\n"
        + "variable a \"Alpha\"\r\n"
        + "variable b \"Beta\"\r\n"
        + "link a -> b +\r\n";

    private const string ChangedText =
        "causal-loop 1\r\n"
        + "variable a \"Alpha\"\r\n"
        + "variable b \"Beta\"\r\n"
        + "variable c \"Gamma\"\r\n"
        + "link a -> b +\r\n";

    // The attempts are the store's own count; only the wait between them is taken away.
    private const int Attempts = CausalLoopDocumentStore.DefaultReadAttempts;

    private readonly string _workspace = IoPath.Combine(IoPath.GetTempPath(), "adp-causal-loop-refused-read-" + Guid.NewGuid().ToString("N"));
    private readonly ScriptedReader _reader = new();

    public CausalLoopDocumentStoreRefusedReadTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AReloadWhoseReadIsRefusedOnce_StillDeliversTheChange()
    {
        // Arrange: a diagram loaded, then changed on disk, and the reload's first read refused.
        var (store, path) = Opened();
        var changes = 0;
        store.Changed += (_, _) => changes++;
        File.WriteAllText(path, ChangedText);
        _reader.Refuse(SharingViolation());
        var readsBefore = _reader.Reads;

        // Act
        store.Reload(path);

        // Assert: the refusal was hit - not the file read some other way.
        Assert.Equal(1, _reader.Refused);
        Assert.Equal(0, _reader.Pending);

        // Assert: the change arrived, and the sessions on it were told. Asserted before the read
        // count, so a store that does not retry fails HERE, on the lost change itself.
        var entry = store.GetOrLoad(path);
        Assert.True(entry.IsUsable, entry.Error);
        Assert.Contains(entry.Model.Variables, variable => variable.Label == "Gamma");
        Assert.Equal(1, changes);

        // Assert: by reading past the refusal once, not by reading again some other way.
        Assert.Equal(2, _reader.Reads - readsBefore);
    }

    [Fact]
    public void AReloadThatFindsTheBodyMissingOnce_StillDeliversTheChange()
    {
        // A publish renaming the body away for an instant: on a reload that is a publish in flight,
        // not a diagram that has gone, so it is retried as a refusal is.
        var (store, path) = Opened();
        File.WriteAllText(path, ChangedText);
        _reader.Refuse(new FileNotFoundException("Gone for an instant.", path));

        store.Reload(path);

        Assert.Equal(1, _reader.Refused);
        Assert.Equal(0, _reader.Pending);
        Assert.Contains(store.GetOrLoad(path).Model.Variables, variable => variable.Label == "Gamma");
    }

    [Fact]
    public void AReloadRefusedOnEveryAttempt_KeepsTheLastGoodDiagram_OnlyOnceTheAttemptsAreSpent()
    {
        // Keeping the last good diagram is still the answer to a refusal that outlasts the retries -
        // but only after every attempt was made, which is what the read count says.
        var (store, path) = Opened();
        var changes = 0;
        store.Changed += (_, _) => changes++;
        File.WriteAllText(path, ChangedText);
        for (var i = 0; i < Attempts; i++)
        {
            _reader.Refuse(SharingViolation());
        }

        var readsBefore = _reader.Reads;

        store.Reload(path);

        Assert.Equal(Attempts, _reader.Reads - readsBefore);
        Assert.Equal(0, _reader.Pending);
        Assert.Equal(0, changes);
        var held = store.GetOrLoad(path);
        Assert.True(held.IsUsable, held.Error);
        Assert.DoesNotContain(held.Model.Variables, variable => variable.Label == "Gamma");
    }

    [Fact]
    public void AFirstOpenWhoseReadIsRefusedOnce_OpensTheDiagram_NotAnUnreadableOne()
    {
        // Before the retry a first open refused once opened as Unreadable, so the diagram showed an
        // error and could not be saved until something reloaded it.
        var path = IoPath.Combine(_workspace, "loop.cld");
        File.WriteAllText(path, Text);
        var store = Store();
        _reader.Refuse(SharingViolation());

        var entry = store.GetOrLoad(path);

        Assert.Equal(1, _reader.Refused);
        Assert.Equal(0, _reader.Pending);
        Assert.True(entry.IsUsable, entry.Error);
        Assert.Contains(entry.Model.Variables, variable => variable.Label == "Alpha");
    }

    [Fact]
    public void AFirstOpenThatFindsTheBodyMissing_IsNotRetried()
    {
        // The must-not-catch half: on a first open a missing body is not a publish in flight, and
        // retrying it would delay every new diagram. One read, and it reports itself unreadable as a
        // missing body - in the store's own words since task 6, not the reader's exception message,
        // by the user's ruling on the decision card (2026-09-26).
        var path = IoPath.Combine(_workspace, "loop.cld");
        File.WriteAllText(path, Text);
        var store = Store();
        _reader.Refuse(new FileNotFoundException("Not there when opened.", path));

        var entry = store.GetOrLoad(path);

        Assert.Equal(1, _reader.Reads);
        Assert.Equal(1, _reader.Refused);
        Assert.False(entry.IsUsable);
        Assert.Contains(CausalLoopDocumentStore.MissingReason, entry.Error, StringComparison.Ordinal);
    }

    private (CausalLoopDocumentStore Store, string Path) Opened()
    {
        var path = IoPath.Combine(_workspace, "loop.cld");
        File.WriteAllText(path, Text);
        var store = Store();
        Assert.True(store.GetOrLoad(path).IsUsable, "The arrangement failed: the diagram did not open.");
        return (store, path);
    }

    private CausalLoopDocumentStore Store() => new(_reader.Read, Attempts, TimeSpan.Zero);

    private static IOException SharingViolation() =>
        new("The process cannot access the file because it is being used by another process.");

    /// <summary>The real shared read, refusing first as often as a test has scripted it to.</summary>
    private sealed class ScriptedReader
    {
        private readonly Queue<Exception> _refusals = new();

        public int Reads { get; private set; }

        public int Refused { get; private set; }

        public int Pending => _refusals.Count;

        public void Refuse(Exception refusal) => _refusals.Enqueue(refusal);

        public string Read(string path)
        {
            Reads++;
            if (_refusals.TryDequeue(out var refusal))
            {
                Refused++;
                throw refusal;
            }

            return SharedDocumentReader.ReadAllText(path);
        }
    }
}
