using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// A read refused for a moment must not cost the change it was reading.
/// </summary>
/// <remarks>
/// The EditorResolution 60-second flake: a .dsl saved, the watcher's reload refused by another
/// holder of the file, the last good model kept - and no later event to re-read it, so the change
/// never arrived. Keeping the last good model assumed the watcher's next event would re-read the
/// finished file, and the last event of a write has no next one. The store now retries a refused
/// read as backend-centralization's DocumentLifecycle does. The refusal is scripted into the read
/// rather than raced for with a handle, so each test refuses exactly as often as it says, waits for
/// nothing, and asserts its refusal was actually consumed - a test that could pass by reading the
/// file some other way would prove nothing.
/// </remarks>
public class C4DocumentStoreRefusedReadTests : IDisposable
{
    private const string Model = """
        workspace "Bank" {
            model {
                u = person "Customer" "A customer."
                s = softwareSystem "Banking" "Does banking."
                u -> s "Uses"
            }
            views {
                systemContext s "context" {
                    include *
                }
            }
        }
        """;

    private const string ChangedModel = """
        workspace "Bank" {
            model {
                u = person "Customer" "A customer."
                a = person "Auditor" "Checks the books."
                s = softwareSystem "Banking" "Does banking."
                u -> s "Uses"
            }
            views {
                systemContext s "context" {
                    include *
                }
            }
        }
        """;

    // The attempts are the store's own count; only the wait between them is taken away.
    private const int Attempts = C4DocumentStore.DefaultReadAttempts;

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.RefusedRead", Guid.NewGuid().ToString("N"));
    private readonly ScriptedReader _reader = new();

    public C4DocumentStoreRefusedReadTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AReloadWhoseReadIsRefusedOnce_StillDeliversTheChange()
    {
        // Arrange: a model loaded, then changed on disk, and the reload's first read refused.
        var (store, body) = Opened();
        C4Workspace? told = null;
        store.Changed += (_, args) => told = args.Workspace;
        File.WriteAllText(body, ChangedModel);
        _reader.Refuse(SharingViolation());
        var readsBefore = _reader.Reads;

        // Act
        store.Reload(body);

        // Assert: the refusal was hit - not the file read some other way.
        Assert.Equal(1, _reader.Refused);
        Assert.Equal(0, _reader.Pending);

        // Assert: the change arrived, to the sessions on it and in the store. Asserted before the
        // read count, so a store that does not retry fails HERE, on the lost change itself.
        Assert.NotNull(told);
        Assert.Contains(told.Elements, element => element.Name == "Auditor");
        Assert.Contains(store.WorkspaceOf(body).Elements, element => element.Name == "Auditor");

        // Assert: by reading past the refusal once, not by reading again some other way.
        Assert.Equal(2, _reader.Reads - readsBefore);
    }

    [Fact]
    public void AReloadThatFindsTheBodyMissingOnce_StillDeliversTheChange()
    {
        // A publish renaming the body away for an instant: on a reload that is a publish in flight,
        // not a model that has gone, so it is retried as a refusal is.
        var (store, body) = Opened();
        File.WriteAllText(body, ChangedModel);
        _reader.Refuse(new FileNotFoundException("Gone for an instant.", body));

        store.Reload(body);

        Assert.Equal(1, _reader.Refused);
        Assert.Equal(0, _reader.Pending);
        Assert.Contains(store.WorkspaceOf(body).Elements, element => element.Name == "Auditor");
    }

    [Fact]
    public void AReloadRefusedOnEveryAttempt_KeepsTheLastGoodModel_OnlyOnceTheAttemptsAreSpent()
    {
        // Keeping the last good model is still the answer to a refusal that outlasts the retries -
        // but only after every attempt was made, which is what the read count says.
        var (store, body) = Opened();
        var changes = 0;
        store.Changed += (_, _) => changes++;
        File.WriteAllText(body, ChangedModel);
        for (var i = 0; i < Attempts; i++)
        {
            _reader.Refuse(SharingViolation());
        }

        var readsBefore = _reader.Reads;

        store.Reload(body);

        Assert.Equal(Attempts, _reader.Reads - readsBefore);
        Assert.Equal(0, _reader.Pending);
        Assert.Equal(0, changes);
        var held = store.WorkspaceOf(body);
        Assert.Contains(held.Elements, element => element.Name == "Customer");
        Assert.DoesNotContain(held.Elements, element => element.Name == "Auditor");
    }

    [Fact]
    public void AFirstOpenWhoseReadIsRefusedOnce_OpensTheModel_NotAnEmptyOne()
    {
        // Before the retry a first open refused once opened EMPTY and marked unreadable, so the
        // diagram showed nothing and could not be saved until something reloaded it.
        var body = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(body, Model);
        var store = Store();
        _reader.Refuse(SharingViolation());

        var workspace = store.WorkspaceOf(body);

        Assert.Equal(1, _reader.Refused);
        Assert.Equal(0, _reader.Pending);
        Assert.Contains(workspace.Elements, element => element.Name == "Customer");
        // The two-argument Save refuses a path marked unreadable, keyed on the path; GetOrLoad only
        // hands it the document WorkspaceOf already cached, so it cannot clear the mark first.
        Assert.Equal("", store.Save(body, store.GetOrLoad(body)));
    }

    [Fact]
    public void AFirstOpenThatFindsTheBodyMissing_IsNotRetried()
    {
        // The must-not-catch half: on a first open a missing body is a new document, and retrying it
        // would delay every new diagram. One read, and it opens empty.
        var body = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(body, Model);
        var store = Store();
        _reader.Refuse(new FileNotFoundException("Not there when opened.", body));

        var workspace = store.WorkspaceOf(body);

        Assert.Equal(1, _reader.Reads);
        Assert.Equal(1, _reader.Refused);
        Assert.Empty(workspace.Elements);
    }

    private (C4DocumentStore Store, string Body) Opened()
    {
        var body = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(body, Model);
        var store = Store();
        Assert.Contains(store.WorkspaceOf(body).Elements, element => element.Name == "Customer");
        return (store, body);
    }

    private C4DocumentStore Store() => new(_reader.Read, Attempts, TimeSpan.Zero);

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
