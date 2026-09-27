using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// Another program republishing an unchanged body while reloads arrive (backend-centralization R2.6,
/// first half): no reload installs an empty or unreadable document.
/// </summary>
/// <remarks>
/// <para>
/// <b>The measurement this guards:</b> c4, before the shared lifecycle, installed an empty workspace in
/// 765 of 3000 reloads while an external program republished an unchanged <c>.dsl</c>, because a
/// reload landing inside that program's <c>File.Replace</c> found the body missing or refused.
/// </para>
/// <para>
/// <b>Driven through <c>Reload</c>, the entry point a store's watcher calls, never through the read
/// helper beneath it.</b> A later reader is far more likely to break the wiring - a reload that stops
/// consulting the retry, or installs its failure on the way out - than the helper itself, and a guard
/// on the helper would stay green through exactly that. Both entry points are driven, because the
/// writable one wraps the read-only one and sparql calls the read-only one directly.
/// </para>
/// <para>
/// <b>Scripted rather than raced.</b> A republish is replayed phase by phase as a reader meets it: the
/// body renamed away, there when asked and gone when opened, refused by the publisher's handle, and
/// finally back unchanged. A real race would not do: on Linux a replace is an atomic rename and never
/// shows a reader a missing body, so a raced guard would pass here against the defect it exists to
/// catch. Each phase outlasts the retries, because a phase the retry absorbs never reaches the
/// decision this guards.
/// </para>
/// <para>
/// The second half of R2.6 - a body really deleted ends as the empty document rather than the last one
/// kept alive - is <c>DocumentLifecycleTests.AMissingBody_IsKeptOnReload_AndClearedOnlyByTheWatchersDelete</c>.
/// </para>
/// </remarks>
public class DocumentLifecycleRepublishTests : IDisposable
{
    private const string Body = "the unchanged body";

    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-lifecycle-republish-" + Guid.NewGuid().ToString("N"));

    public DocumentLifecycleRepublishTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ARepublishOfAnUnchangedBody_NeverInstallsAnEmptyOrUnreadableDocument(bool writable, bool declaresUnavailability)
    {
        // Both kinds of module: one that opens an unreadable body EMPTY, and one that reports it as a
        // state of its own (causal-loop, sparql). Either would be what a planted install-on-unreadable
        // puts on the canvas, so neither may appear.
        var path = IoPath.Combine(_folder, "plan.note");
        File.WriteAllText(path, Body);
        var publisher = new Publisher(path);
        var store = Store.Over(writable, declaresUnavailability, publisher.Read);
        var good = store.GetOrLoad(path);
        Assert.Equal(Body, good.Text);

        foreach (var phase in new[] { Phase.RenamedAway, Phase.GoneWhenOpened, Phase.Refused })
        {
            publisher.Enter(phase);

            var announced = store.Reload(path);

            // The document first, so a failure names what was installed rather than only that
            // something was.
            Assert.Equal(Body, store.Get(path)?.Text);
            Assert.Same(good, store.Get(path));
            Assert.False(announced, $"a reload inside the publish's {phase} phase announced a change");
        }

        // The phases were really met: without this, a reload that never consulted the scripted read
        // would pass by never seeing the publish at all.
        Assert.Equal(Attempts, publisher.ReadsIn(Phase.GoneWhenOpened));
        Assert.Equal(Attempts, publisher.ReadsIn(Phase.Refused));

        publisher.Enter(Phase.Published);

        store.Reload(path);

        // The control: the reload after the publish reads the body back, so the kept document above is
        // the lifecycle waiting out the publish rather than a reload that never installs anything.
        Assert.Equal(Body, store.Get(path)?.Text);
    }

    // The injected retry bound, not production's: every phase outlasts it.
    private const int Attempts = 3;

    private enum Phase
    {
        Published,
        RenamedAway,
        GoneWhenOpened,
        Refused,
    }

    /// <summary>
    /// Another program's republish of the same text, as a reader of <c>path</c> meets it.
    /// </summary>
    private sealed class Publisher(string path)
    {
        private readonly string _aside = path + ".publishing";
        private readonly Dictionary<Phase, int> _reads = [];
        private Phase _phase = Phase.Published;

        public void Enter(Phase phase)
        {
            // The rename is on disk, so the lifecycle's own existence check meets it, not only its read.
            if (phase == Phase.RenamedAway)
            {
                File.Move(path, _aside);
            }
            else if (_phase == Phase.RenamedAway)
            {
                File.Move(_aside, path);
            }

            _phase = phase;
        }

        public int ReadsIn(Phase phase) => _reads.GetValueOrDefault(phase);

        public string Read(string file)
        {
            _reads[_phase] = ReadsIn(_phase) + 1;
            return _phase switch
            {
                Phase.GoneWhenOpened => throw new FileNotFoundException("renamed away between the check and the open", file),
                Phase.Refused => throw new IOException("The process cannot access the file because it is being used by another process."),
                _ => File.ReadAllText(file),
            };
        }
    }

    /// <summary>The two lifecycles a store can hold, behind the calls its watcher and sessions make.</summary>
    private sealed class Store(Func<string, Note> getOrLoad, Func<string, Note?> get, Func<string, bool> reload)
    {
        public Note GetOrLoad(string path) => getOrLoad(path);

        public Note? Get(string path) => get(path);

        public bool Reload(string path) => reload(path);

        public static Store Over(bool writable, bool declaresUnavailability, Func<string, string> read)
        {
            var unavailable = declaresUnavailability ? Unavailable : null;
            if (writable)
            {
                var lifecycle = new WritableDocumentLifecycle<Note>(Parse, note => note.Text, unavailable, read, Attempts, TimeSpan.Zero);
                return new Store(lifecycle.GetOrLoad, lifecycle.Get, lifecycle.Reload);
            }

            var readOnly = new DocumentLifecycle<Note>(Parse, unavailable, read, Attempts, TimeSpan.Zero);
            return new Store(readOnly.GetOrLoad, readOnly.Get, readOnly.Reload);
        }
    }

    private static readonly Func<string, string, Note> Parse = (_, text) => new Note(text);

    private static readonly Func<string, DocumentUnavailability, string, Note> Unavailable =
        (_, unavailability, _) => new Note($"unavailable: {unavailability}");

    private sealed class Note(string text)
    {
        public string Text { get; } = text;
    }
}
