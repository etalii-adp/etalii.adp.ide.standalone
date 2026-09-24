using System.Collections.Concurrent;
using System.Diagnostics;
using EtAlii.Adp.Authentication;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.C4;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Projects;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;
using Path = EtAlii.Adp.Common.Wire.Path;
using Service = EtAlii.Adp.Diagram.DiagramService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// <c>UpdateView</c> returns while the diagram's <c>Open</c> stream is live on the same
/// connection - the pairing no test exercised, and the condition a wedged client was in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap.</b> Module sessions are tested directly and flow tests open streams, but nothing
/// called the <c>UpdateView</c> RPC while an <c>Open</c> stream was registered. That matters
/// because <c>UpdateView</c> does not compute anything itself: it looks the connection up in the
/// viewport registry and invokes the callback the OPEN STREAM registered, synchronously, on the
/// <c>UpdateView</c> request thread. Without a live stream there is no callback and the call
/// returns through a guard clause, so a test without one measures the wrong thing while looking
/// identical in a pass list.
/// </para>
/// <para>
/// <b>What was already measured, so this is not a repeat.</b> C4's own
/// <c>IDiagramSession.UpdateView</c> answers the wedged document in 2 ms single-threaded with no
/// stream (<c>C4Session.UpdateViewOnTheRealCorpus.Tests</c>). This drives the same document
/// through the RPC with the stream registered, which is the only server-side arrangement left
/// that nobody had exercised.
/// </para>
/// <para>
/// <b>The vacuity guard, and it is the whole design.</b> <c>Open</c> registers with the viewport
/// registry at <c>DiagramService.Open.cs:119</c> and deregisters in that method's <c>finally</c>.
/// Calling <c>UpdateView</c> before the registration exists - or after it is gone - makes
/// <c>Report</c> return false and the RPC answer "The diagram is not open on this connection."
/// INSTANTLY and successfully. Green, fast, and it would have tested a guard clause. So this waits
/// on <see cref="IDiagramViewportRegistry.Find"/> returning non-null and ASSERTS it, rather than
/// waiting for <c>Open</c> to have been called or sleeping. A fast return is only evidence once
/// the registration is known to have been held.
/// </para>
/// <para>
/// <b>What it cannot show.</b> There is no HTTP here, so Kestrel, the grpc-web translation and the
/// <b>And the field condition it is named for has moved under it.</b> The browser-side evidence
/// that <c>UpdateView</c> was dispatched at all was withdrawn: what was measured as "in flight"
/// counted QUEUED requests, and the origin appears to have stopped dispatching some 250 ms
/// BEFORE <c>UpdateView</c> was issued - which would make its non-return a symptom rather than
/// the first domino, with the <c>Open</c> stream the likelier trigger. That does not touch what
/// this test measures, which is a fact about the server and true whatever the browser did. It
/// does mean a reader should not take this file as evidence about the browser at all.
/// </para>
/// <para>
/// browser's transport are all untouched - and the field symptom, "nothing on that origin completes
/// afterwards", is defined at a layer this cannot see. A hang here would be a positive
/// reproduction and strong; a clean run is weak, and its natural reading - "therefore the client" -
/// is the unsafe one.
/// </para>
/// </remarks>
public sealed class UpdateViewWhileTheStreamIsOpenTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _appData = Directory.CreateTempSubdirectory("adp-rpc-appdata-").FullName;
    private readonly string _projectFolder = Directory.CreateTempSubdirectory("adp-rpc-project-").FullName;
    private readonly ServiceProvider _provider;
    private readonly Service _service;
    private readonly IDiagramViewportRegistry _viewports;
    private readonly IProjectStore _projects;

    public UpdateViewWhileTheStreamIsOpenTests()
    {
        CopyCorpus();

        var services = new ServiceCollection();
        services
            .AddProjects(_appData)
            .AddContext()
            .AddHierarchy()
            .AddDiagrams()
            .AddCommands()
            .AddHierarchyCommandHandlers()
            .AddC4();
        // The host discovers these at startup and registers them before anything can serve a
        // request; the file router resolves an .adp to its module through them, so a container
        // without them cannot open a diagram at all.
        IReadOnlyList<Common.DiagramDefinition> definitions = DiagramDefinitionDiscovery.Discover();
        services.AddSingleton(definitions);
        services.AddSingleton<Service>();

        _provider = services.BuildServiceProvider();
        _service = _provider.GetRequiredService<Service>();
        _viewports = _provider.GetRequiredService<IDiagramViewportRegistry>();
        _projects = _provider.GetRequiredService<IProjectStore>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TryDelete(_projectFolder);
        TryDelete(_appData);
    }

    [Fact]
    public async Task UpdateView_WhileTheOpenStreamIsRegistered_Returns()
    {
        // Arrange. A project over the real corpus, and the document the wedged client opened.
        var userId = ShortGuid.NewShortGuid();
        var project = _projects.Add(userId, "wedge", new PathRecord(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)));
        var watchId = ShortGuid.NewShortGuid();

        var registration = new Path();
        registration.Segments.Add("bottling-mes.plant-landscape.adp");

        // Act, part one. Open streams until cancelled, so it runs alongside rather than being
        // awaited - exactly as it does for a browser holding the stream open.
        using var openCancellation = new CancellationTokenSource();
        var writer = new CollectingWriter();
        var openContext = CallContext("/etalii.adp.DiagramService/Open", openCancellation.Token);
        SessionContext.SetUserId(openContext, userId);
        var open = Task.Run(
            () => _service.Open(
                new OpenDiagramRequest { ProjectId = project.Id, WatchId = watchId, Path = registration },
                writer,
                openContext),
            TestContext.Current.CancellationToken);

        // THE VACUITY GUARD. Not "Open was called" and not a sleep: the registration must be HELD,
        // because UpdateView reaches the module only through it. Without this the call returns
        // "The diagram is not open on this connection" in microseconds and the run looks green.
        var bodyPath = IoPath.Combine(_projectFolder, "bottling-mes.dsl");
        // BOTH conditions, because the registration alone is not a live stream: Open registers
        // BEFORE it streams the baseline, so a wait on the registration can win the race and
        // measure a stream that has produced nothing. Caught by the writer check below on the
        // first run of this harness, which is exactly the weakness it was written to catch.
        var registered = await Eventually(() => _viewports.Find(watchId, bodyPath) is not null && writer.Count > 0);
        Assert.True(
            registered,
            $"The Open stream never registered a viewport for {bodyPath}, so UpdateView would have answered its guard clause and this test would have measured nothing. Open faulted: {open.Exception?.GetBaseException().Message ?? "no"}.");

        // Act, part two. The call that DID NOT RETURN in the field - carefully not "never
        // returned", because whether it was ever dispatched is unestablished: the browser
        // measurement that appeared to show it on the wire turned out to record OUTSTANDING
        // rather than SENT, and a queued request that never leaves is not a call that failed to
        // return. On the same connection, with
        // the stream live. Timed on a worker so a hang is REPORTED rather than hanging the suite.
        var clock = Stopwatch.StartNew();
        var update = Task.Run(
            () =>
            {
                var context = CallContext("/etalii.adp.DiagramService/UpdateView", TestContext.Current.CancellationToken);
                SessionContext.SetUserId(context, userId);
                return _service.UpdateView(
                    new UpdateViewRequest
                    {
                        ProjectId = project.Id,
                        WatchId = watchId,
                        Path = registration,
                        View = new ViewUpdate
                        {
                            BoundingBox = new BoundingBox
                            {
                                Min = new Point2D { X = 0, Y = 0 },
                                Max = new Point2D { X = 4000, Y = 3000 },
                            },
                        },
                    },
                    context);
            },
            TestContext.Current.CancellationToken);

        var returned = await Task.WhenAny(update, Task.Delay(Patience, TestContext.Current.CancellationToken)) == update;
        clock.Stop();

        // Assert.
        Assert.True(
            returned,
            $"UpdateView did not return within {Patience.TotalSeconds:0}s with the Open stream registered. THAT IS THE WEDGE, reproduced with no browser and no transport: the hang is server-side.");

        var response = await update;
        Assert.True(
            string.IsNullOrEmpty(response.Error),
            $"UpdateView returned, but refused: '{response.Error}'. A refusal is not the measurement - it means the call never reached the module, so the timing says nothing.");
        // A THIRD NON-VACUITY CHECK: the stream is not merely registered, it has streamed. Open
        // writes the baseline before it starts waiting, so an empty writer would mean the
        // session never produced anything and the registration was the only thing that happened.
        Assert.True(
            writer.Count > 0,
            "The Open stream registered but wrote nothing, so the session produced no baseline and the timing below is not about a live stream.");

        Assert.True(
            clock.Elapsed < Patience,
            $"MEASURED: UpdateView returned in {clock.Elapsed.TotalMilliseconds:0} ms with the Open stream registered and {writer.Count} messages streamed.");

        await openCancellation.CancelAsync();
    }

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        return false;
    }

    private static ServerCallContext CallContext(string method, CancellationToken cancellationToken) =>
        TestServerCallContext.Create(
            method: method,
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(5),
            requestHeaders: new Metadata(),
            cancellationToken: cancellationToken,
            peer: "test-peer",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => WriteOptions.Default,
            writeOptionsSetter: _ => { });

    private void CopyCorpus()
    {
        var source = IoPath.Combine(RepositoryRoot(), "src", "diagrams", "c4", "examples", "industrial-plant", "architecture");
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, IoPath.Combine(_projectFolder, IoPath.GetFileName(file)));
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "diagrams")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root could not be found from " + AppContext.BaseDirectory);
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder something still holds is not a test failure.
        }
    }

    /// <summary>The stream's far end, which only has to accept what Open writes.</summary>
    private sealed class CollectingWriter : IServerStreamWriter<Delta>
    {
        private readonly ConcurrentQueue<Delta> _written = new();

        public WriteOptions? WriteOptions { get; set; }

        public int Count => _written.Count;

        public Task WriteAsync(Delta message)
        {
            _written.Enqueue(message);
            return Task.CompletedTask;
        }

        /// <summary>
        /// The CANCELLABLE overload, which is the one Open actually calls. Leaving it to the
        /// interface default throws "Cancellation of stream writes is not supported by this gRPC
        /// implementation" - which faulted Open silently on this harness's first live run, and was
        /// visible only because the registration wait reports what Open did when it times out.
        /// </summary>
        public Task WriteAsync(Delta message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return WriteAsync(message);
        }
    }
}
