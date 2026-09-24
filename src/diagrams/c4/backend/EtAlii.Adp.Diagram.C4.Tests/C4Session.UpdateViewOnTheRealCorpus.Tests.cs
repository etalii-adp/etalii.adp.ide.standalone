using System.Diagnostics;
using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// <c>UpdateView</c> returns, on the document a wedged client was looking at when it stopped
/// returning.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this document and not a fixture.</b> On 2026-09-23 a browser tab wedged after opening
/// <c>bottling-mes.plant-landscape.adp</c>: <c>DiagramService/Open</c> answered 200,
/// <c>DiagramService/UpdateView</c> was issued and never returned, and from that moment nothing
/// on that origin completed again - not the next <c>Select</c>, not a later <c>ListEntries</c>,
/// not a plain <c>GET /favicon.ico</c> - while <c>curl</c> answered the same backend in 4.7ms.
/// Reproduced on two independent fresh tabs by Architect 2.
/// </para>
/// <para>
/// <b>What this measures, and what it cannot.</b> Reading the server path narrows a hang to one
/// place: the <c>UpdateView</c> RPC is unary and fully synchronous, and reaches
/// <c>IDiagramSession.UpdateView</c> synchronously on the gRPC request thread through the
/// viewport registry's <c>Report</c> and <c>Apply</c> - whose channel write is unbounded and so
/// cannot block. This exercises exactly that call, on exactly that document, with no host and no
/// transport. It therefore DISCRIMINATES rather than reproduces: if the call returns promptly
/// here, the module's view logic is exonerated and the cause lies at or above the transport,
/// which is where the browser measurement then points. It cannot name a transport cause, and a
/// clean run here is not evidence that the client is at fault - only that this half is not.
/// </para>
/// <para>
/// <b>The corpus is copied, never opened in place.</b> A session writes a layout sidecar beside
/// the body, and the vendored examples are test subjects whose bytes other guards compare.
/// </para>
/// </remarks>
public class C4SessionUpdateViewOnTheRealCorpusTests : IDisposable
{
    /// <summary>
    /// Long enough that a slow machine never reds this, short enough that a genuine hang is not
    /// mistaken for slowness. The wedged call did not return in minutes.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "adp-c4-wedge-" + Guid.NewGuid().ToString("N"));
    private readonly C4DocumentStore _documents = new();
    private readonly C4ElementMapper _mapper = new(C4Metrics.Default, new C4LayoutSidecar());
    private readonly IHistoryStackStore _historyStacks = new ServiceCollection()
        .AddCommands()
        .AddHierarchyCommandHandlers()
        .AddC4()
        .BuildServiceProvider()
        .GetRequiredService<IHistoryStackStore>();

    public C4SessionUpdateViewOnTheRealCorpusTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task UpdateView_OnTheDocumentThatWedgedAClient_Returns()
    {
        // Arrange: the real document, copied out of the examples so the session's sidecar write
        // lands in a scratch folder.
        var body = CopyCorpus();
        var registration = IoPath.ChangeExtension(body, null) + ".plant-landscape.adp";
        Assert.True(File.Exists(registration), $"The registration the wedged client opened is missing: {registration}");

        await using var session = Open(body, registration);
        var baseline = session.Baseline();

        // NOT VACUOUS: a document that failed to parse yields an empty session whose
        // UpdateView returns instantly, which would pass every timing assertion below while
        // measuring nothing. The corpus must actually be loaded for the measurement to mean
        // anything, so that is asserted before it is taken.
        Assert.NotEmpty(baseline);

        // Act: the viewport report the client sends once its canvas has a size - the call that
        // never returned. Timed on this thread, exactly as the RPC would run it.
        var clock = Stopwatch.StartNew();
        var deltas = RunWithin(() => session.UpdateView(new DiagramViewport(0, 0, 4000, 3000)), "the first view report");
        var first = clock.Elapsed;

        // And again, narrowed - a pan or a zoom, the second report of an ordinary session.
        clock.Restart();
        _ = RunWithin(() => session.UpdateView(new DiagramViewport(500, 500, 1500, 1200)), "a narrowed view report");
        var second = clock.Elapsed;

        // Assert: it returned. The numbers go in the record because "fast" is the finding - a
        // module that answered in milliseconds cannot be what held a request open for minutes.
        Assert.True(
            first < Patience && second < Patience,
            $"UpdateView took {first.TotalMilliseconds:0} ms then {second.TotalMilliseconds:0} ms against {Patience.TotalSeconds:0}s of patience.");

        // The measurement itself, kept where a later reader meets it: the baseline size is
        // what makes the timings comparable to the wedged run rather than to a toy.
        Assert.True(
            baseline.OfType<DiagramAddDelta>().Sum(add => add.Elements.Count) > 1 && first < Patience,
            $"MEASURED: baseline {baseline.Count} deltas carrying {baseline.OfType<DiagramAddDelta>().Sum(add => add.Elements.Count)} elements ({string.Join(", ", baseline.Select(d => d.GetType().Name))}), first view report {first.TotalMilliseconds:0} ms, narrowed report {second.TotalMilliseconds:0} ms, {deltas.Count} deltas from the first report.");
        Assert.NotNull(deltas);
        Assert.NotEmpty(baseline);
    }

    /// <summary>
    /// Runs the call on a worker and fails with a sentence rather than hanging the suite, so a
    /// genuine hang is REPORTED instead of killing the run - the distinction this whole
    /// investigation turns on.
    /// </summary>
    private static IReadOnlyList<DiagramDelta> RunWithin(Func<IReadOnlyList<DiagramDelta>> call, string what)
    {
        var work = Task.Run(call);
        if (!work.Wait(Patience))
        {
            throw FailException.ForFailure(
                $"UpdateView did not return within {Patience.TotalSeconds:0}s on {what}. That is the wedge, reproduced with no browser and no transport: the hang is SERVER-SIDE, in this module's view logic.");
        }

        return work.Result;
    }

    private string CopyCorpus()
    {
        var source = IoPath.Combine(ExamplesRoot(), "industrial-plant", "architecture");
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, IoPath.Combine(_root, IoPath.GetFileName(file)));
        }

        return IoPath.Combine(_root, "bottling-mes.dsl");
    }

    private C4Session Open(string bodyPath, string? registrationPath) =>
        (C4Session)new C4SessionFactory(new DiagramOrigin("c4", "system-landscape"), _documents, _mapper, _historyStacks)
            .Open(ShortGuid.NewShortGuid(), _root, bodyPath, registrationPath);

    private static string ExamplesRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "examples");
            if (Directory.Exists(candidate) && Directory.Exists(IoPath.Combine(directory.FullName, "backend")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("The c4 module's examples folder could not be found from " + AppContext.BaseDirectory);
    }
}
