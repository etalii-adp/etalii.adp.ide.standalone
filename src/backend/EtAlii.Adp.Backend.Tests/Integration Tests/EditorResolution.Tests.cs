using System.Text;
using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Common.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Diagrams-first resolution over the real host (modular-text-editors Requirements 5.1, 3.1,
/// 5.4). The regression this guards, specifically: a bare <c>.dsl</c> with NO <c>.adp</c>
/// registration anywhere still opens as a C4 diagram, because <c>.dsl</c> is a non-shared
/// extension the router claims on sight - the one behaviour a text-editor family most
/// plausibly breaks by being consulted first. The assertion tells the two outcomes apart by
/// what streams: a diagram baselines its own elements, an editor baselines exactly one
/// synthetic <c>"content"</c> element.
/// </summary>
public class EditorResolutionTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";

    private readonly string _appDataRoot;
    private readonly string _projectFolder;
    private readonly WebApplicationFactory<Program> _factory;

    public EditorResolutionTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        // A bare .dsl - no .adp beside it, deliberately: adding one would test the wrong
        // scenario - and a file no diagram type claims.
        File.WriteAllText(
            IoPath.Combine(_projectFolder, "design.dsl"),
            "workspace \"W\" {\n  model {\n    p = person \"P\"\n  }\n  views {\n    systemLandscape \"sl\" {\n      include *\n    }\n  }\n}\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "notes.txt"), "just some notes\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<Common.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public async Task ABareUnregisteredDsl_StillOpensAsAC4Diagram()
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var diagramClient = new DiagramService.DiagramServiceClient(channel);

        // Act.
        var firstAdd = await FirstAddDeltaAsync(diagramClient, headers, projectId, "design.dsl");

        // Assert: the baseline is the DIAGRAM's own elements - were resolution ever inverted,
        // the editor's one synthetic "content" element would arrive here instead.
        Assert.NotEmpty(firstAdd.Elements);
        Assert.DoesNotContain(firstAdd.Elements, element => element.Id?.Value == "content");
    }

    [Fact]
    public async Task AFileNoDiagramClaims_OpensInThePlainEditor()
    {
        // Arrange (Requirements 3.1, 5.4: no registration, no ceremony - the fallback answers).
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var diagramClient = new DiagramService.DiagramServiceClient(channel);

        // Act.
        var firstAdd = await FirstAddDeltaAsync(diagramClient, headers, projectId, "notes.txt");

        // Assert: one synthetic element carrying the whole file.
        var element = Assert.Single(firstAdd.Elements);
        Assert.Equal("content", element.Id?.Value);
        Assert.Equal("editor/plain", element.Type);
        Assert.Equal("just some notes\n", Encoding.UTF8.GetString(element.Payload!.Value.Span));
    }

    // THIS TEST HAS A SIXTY-SECOND gRPC DEADLINE AND IT IS SET IN THE ARRANGE BLOCK BELOW:
    // `deadline: DateTime.UtcNow.AddSeconds(60)`, a named argument on the Open calls that create
    // `diagramCall` and `textCall`. NextAddAsync's own docstring says the same thing in words.
    //
    // It is cited by NAME rather than by line, because the first draft of this correction said
    // "lines 195 and 201" and shortening the comment moved them to 190 and 196 before the file was
    // saved. **A comment that cites line numbers in its own file invalidates itself whenever its own
    // length changes** - the same defect it is here to correct, one turn later.
    //
    // THIS COMMENT USED TO SAY THE SIXTY SECONDS WAS SET NOWHERE IN THIS REPOSITORY. That was WRONG
    // WHEN WRITTEN rather than overtaken, and the difference matters: the deadlines entered on
    // 2026-09-01 in a84d12ad and the claim on 2026-09-24 in cf419d02, twenty-three days apart.
    //
    // THE MECHANISM OF THE MISS IS WORTH MORE THAN THE FINDING, and it is one character run. The
    // search was for `FromSeconds(60)`; the code says `AddSeconds(60)` - one builds a TimeSpan, the
    // other offsets a DateTime, and a search is correct about the string it was given. THE TELL WAS
    // IN THE RESULT ITSELF: that grep's only hit anywhere in src/backend was this comment asserting
    // the absence. **A search whose sole result is the claim that the thing is not there is the
    // signature of a search that missed** - and it reads exactly like confirmation.
    //
    // WHAT STILL STANDS, BECAUSE IT WAS MEASURED RATHER THAN SEARCHED:
    //
    //   * the HttpClient's timeout is 00:03:20 - two hundred seconds, logged on eleven consecutive
    //     runs - so the sixty-second deadline is what fires first, as the reasoning predicted;
    //   * ten isolated runs, instrumented, ten greens. Alone, the chain finishes well inside sixty.
    //
    // Both eliminations survive intact. The third was a string search rather than a measurement, and
    // **a string search has to be re-run rather than cited, because its blind spot is invisible from
    // its own result.** Do not read the two kinds at one confidence again.
    //
    // THE FAILURE RATE, board-wide rather than from one worktree: 4 failures in 58 gate runs that
    // reached the backend suite, across seven scratch worktrees. An earlier note here said 3 in 9 -
    // one worktree's history, read as if it were everyone's.
    //
    // AND THE THIRTY-SECOND SITES HAVE NEVER FAILED. Five explicit deadlines exist in the tree: these
    // two sixties, and three thirties - `boomCall` and `notesCall` in EditorModuleIsolation.Tests.cs,
    // and FirstAddDeltaAsync's own call at the foot of this file. In those same 58 runs
    // EditorModuleIsolationTests failed ZERO times, and its eleven appearances in those logs are all
    // stack-trace lines from its own deliberately-throwing fixture - which is also the proof that it
    // ran rather than being skipped, since a passing test prints nothing at all.
    //
    // IF GENERAL HOST CONTENTION WERE THE CAUSE, THE SHORTER BUDGETS WOULD FIRE FIRST AND OFTENER.
    // They never fire. So the cause is THIS test's wait chain rather than the suite being slow - but
    // **the discriminator narrows rather than closes**, because a thirty-second budget on a short
    // chain is not a fair comparison with a sixty-second budget on a long one. What would close it:
    // put thirty seconds on THIS test's calls and see whether the rate rises.
    //
    // AND THE CHAIN IS THE LIKELY WHY. Five sequential stream awaits share two call deadlines, and a
    // per-call deadline's clock starts when the CALL is made, not when the await begins. The text
    // call carries three of those five awaits, plus a SaveText round trip, plus an external file
    // write, plus the watcher latency that follows it - all inside one sixty seconds that began
    // before any of it.
    //
    // A BETTER SHAPE IS AVAILABLE NOW, AND THE REASON THIS COMMENT ONCE GAVE FOR HOLDING IT BACK WAS
    // WRONG. It said AddDiagramFlow's per-message timeout would make this fail MORE often, its budget
    // being ten seconds against this sixty. **That is true of AddDiagramFlow's NUMBER and false of the
    // SHAPE.** A per-message timeout gives each await its own budget instead of sharing one across the
    // chain, so the same shape at sixty seconds per message is STRICTLY MORE GENEROUS than today. The
    // budget is a free parameter, and the ten seconds is AddDiagramFlow's choice for its own chain.
    //
    // It also keeps the test honest, which a raised call deadline would not: a view that never arrives
    // still fails, and the message names WHICH view, where a deadline raised until nothing can exceed
    // it is a vacuous test wearing a fixed one's clothes.
    //
    // ===== MEASURED 2026-09-24, AND IT FORECLOSES THE REPAIR EVERYBODY REACHES FOR FIRST =====
    //
    // THE CHAIN IS HALF A SECOND. Per-step elapsed times, three runs of the built xunit v3 executable
    // ALONE - not the gate's invocation, so these are a lower bound rather than the number under
    // contention:
    //
    //     login + add project     405 / 399 / 393 ms
    //     1 diagram baseline       95 /  94 /  93 ms
    //     2 text baseline           7 /   8 /   7 ms
    //     3 SaveText rpc           17 /  11 /  11 ms
    //     4 text hears save         0 /   0 /   0 ms    <- the await that fails, 4 times of 4
    //     5 diagram hears save      0 /   0 /   0 ms
    //     6 text hears ext write    4 /   5 /   3 ms
    //     TOTAL                   531 / 520 / 511 ms
    //
    // SIXTY SECONDS IS 113 TIMES THE WHOLE CHAIN, and the await that fails costs ZERO. So WHATEVER
    // THE CAUSE IS, IT IS NOT THE CHAIN BEING SLOW - a marginal budget would also scatter failures
    // across whichever step happened to be slow, and all four are on one. Raising the deadline is the
    // repair this measurement forecloses.
    //
    // AND STEP 4's ZERO IS STRUCTURAL RATHER THAN LUCKY: SaveTextAsync does not return until the write
    // is complete, so the notification is always raised before the await begins. By the time this test
    // reaches step 4 the answer is either already there or it never will be - there is no waiting, and
    // the sixty seconds only decides how long the nothing lasts.
    //
    // FIVE MECHANISMS WERE PROPOSED AND NONE REPRODUCES. Recorded so nobody spends the day again:
    //
    //   1. a dropped notification          INJECTED, green - two arrive per write, so losing one is
    //                                      survivable; each write raises Renamed+Renamed (the save)
    //                                      and Changed+Changed (the external write), measured
    //   2. a not-exists window around the  INJECTED at 1.5 s, green - and the RPC BLOCKS through the
    //      replace consuming the refusal   window, so the test is never waiting during it
    //   3. a stale read superseded late    not run: it needs a second notification to rescue the
    //                                      first, which is the thing in question
    //   4. the enable-before-subscribe     INJECTED at 3 s, green, 4 of 4 notifications RECEIVED -
    //      gap in the watcher              and excluded outright, because its only possible symptom
    //                                      is at step 2, never step 4
    //   5. a subscription-ordering census  RETRACTED by its author: the mechanism cannot arise, the
    //                                      control arm was three components with no tests, and the
    //                                      supporting instance was a test renamed and fixed a day
    //                                      earlier at 09929541
    //
    // WHAT LANDED INSTEAD IS DIAGNOSTICS AND FOUR CORRECTNESS FIXES, none of them claimed as the
    // cause: NextAddAsync now names what it discarded, a refused re-read no longer consumes the
    // change, every watcher is enabled after its handlers and reports its own overflow, and
    // PlainEditorSession's logger is resolved at the call site so its refusal record cannot land in a
    // silent sink. THE MECHANISM REMAINS UNKNOWN. The next occurrence is the first one that will say
    // something, because until now four of them said nothing at all.
    //
    // This comment records what is known. The repair is owned elsewhere (37bb3e06) and the number it
    // lands on should be chosen on purpose rather than inherited.
    [Fact]
    public async Task ADslOpenAsDiagramAndAsTextAtOnce_BothStayTrueToTheFile()
    {
        // Arrange: one connection, two views of one file (Requirements 5.3, 5.5). No
        // synchronisation code exists between them anywhere - each session independently
        // reads and writes the same path, so the file on disk is the tie-breaker by
        // construction, and this test is the proof that construction suffices.
        var originalText = "workspace \"W\" {\n  model {\n    p = person \"Postman\"\n  }\n  views {\n    systemLandscape \"sl\" {\n      include *\n    }\n  }\n}\n";
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "both.dsl"), originalText, TestContext.Current.CancellationToken);

        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        var watchId = ShortGuid.NewShortGuid();
        var path = new Path();
        path.Segments.Add("both.dsl");

        // The diagram view: the router claims the path, exactly as it always has (R5.1).
        using var diagramCall = diagramClient.Open(new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = path }, headers, deadline: DateTime.UtcNow.AddSeconds(60), cancellationToken: TestContext.Current.CancellationToken);
        var diagramBaseline = await NextAddAsync(diagramCall.ResponseStream, add => add.Elements.Count > 0, "the diagram baseline");
        Assert.DoesNotContain(diagramBaseline.Elements, element => element.Id?.Value == "content");

        // The text view of the same path, on the same connection: editor_id forces the editor
        // family - the "Open as text" tab's stream (R5.2) - and both stay open at once.
        using var textCall = diagramClient.Open(new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = path, EditorId = "*" }, headers, deadline: DateTime.UtcNow.AddSeconds(60), cancellationToken: TestContext.Current.CancellationToken);
        var textBaseline = await NextAddAsync(textCall.ResponseStream, add => add.Elements.Any(element => element.Id?.Value == "content"), "the text baseline");
        Assert.Equal(originalText, ContentOf(textBaseline));

        // Act 1: save through the text wire, renaming the person.
        var editedText = originalText.Replace("Postman", "Quartermaster");
        var saved = await diagramClient.SaveTextAsync(
            new SaveTextRequest { ProjectId = projectId, WatchId = watchId, Path = path, Content = editedText },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", saved.Error);

        // Assert 1a: the text session hears its own file change from disk - a Remove+Add
        // replacement, not a stale private copy.
        var textAfterSave = await NextAddAsync(textCall.ResponseStream, add => ContentOf(add) == editedText, "the text view to hear the SaveText write");
        Assert.Equal(editedText, ContentOf(textAfterSave));

        // Assert 1b: R5.3's other half - the diagram hears the same save as pushed deltas.
        // The DiagramDocumentReloadBridge sees the write land, the C4 store re-reads the
        // body, and the open diagram stream re-delivers its view with the renamed person in
        // it. (Task 6.2 recorded a design gap here - every store exposed Reload but nothing
        // in the codebase invoked it, so an external write never reached an open diagram -
        // and the bridge is what closed it.)
        var diagramAfterSave = await NextAddAsync(
            diagramCall.ResponseStream,
            add => add.Elements.Any(element => PayloadTextOf(element).Contains("Quartermaster", StringComparison.Ordinal)),
            "the diagram to hear the SaveText write");
        Assert.DoesNotContain(diagramAfterSave.Elements, element => element.Id?.Value == "content");

        // Act 2 and assert 2: the reverse direction. A diagram-side save lands on the same
        // disk path through its own store; the write below takes that identical
        // watcher-observed route, and the text session reconciles rather than clinging to
        // what it last streamed (R5.5: the disk is the tie-breaker, always).
        var diagramSideText = editedText.Replace("Quartermaster", "Quartermistress");
        await File.WriteAllTextAsync(
            IoPath.Combine(_projectFolder, "both.dsl"), diagramSideText, TestContext.Current.CancellationToken);
        await NextAddAsync(textCall.ResponseStream, add => ContentOf(add) == diagramSideText, "the text view to hear the external write");
    }

    /// <summary>The "content" element's text, or empty for an Add that carries none.</summary>
    private static string ContentOf(Add add)
    {
        var element = add.Elements.FirstOrDefault(candidate => candidate.Id?.Value == "content");
        return element?.Payload is { } payload ? Encoding.UTF8.GetString(payload.Value.Span) : "";
    }

    /// <summary>
    /// An element's payload bytes as text - enough to find a name inside a serialized proto
    /// payload, which carries its strings as UTF-8 verbatim.
    /// </summary>
    private static string PayloadTextOf(Element element) =>
        element.Payload is { } payload ? Encoding.UTF8.GetString(payload.Value.Span) : "";

    // THE GUARD ON NextAddAsync's REPORT. Two tests rather than one, because the value of the
    // report is telling the two cases APART: a stream that said nothing, and a stream that said
    // the wrong thing. A single test would pass on a report unable to distinguish them.
    //
    // Both fail against the previous NextAddAsync, which threw "The stream ended before the
    // expected Add delta arrived." for either case - seen to fail before being believed.

    [Fact]
    public async Task WhenDeltasArriveAndNoneMatch_TheFailureNamesEveryOneItDiscarded()
    {
        var remove = new Delta { Remove = new Remove() };
        var wrongAdd = new Delta { Add = new Add() };
        wrongAdd.Add.Elements.Add(new Element { Id = new ElementId { Value = "not-content" }, Type = "x" });
        var stream = new ScriptedDeltaStream([remove, wrongAdd]);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NextAddAsync(stream, _ => false, "something that never comes"));

        Assert.Contains("something that never comes", failure.Message, StringComparison.Ordinal);
        Assert.Contains("discarded 2 message(s)", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Remove", failure.Message, StringComparison.Ordinal);
        Assert.Contains("not-content", failure.Message, StringComparison.Ordinal);
        Assert.Contains("<no content element>", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenNothingArrivesAtAll_TheFailureSaysSoRatherThanShowingAnEmptyList()
    {
        var stream = new ScriptedDeltaStream([]);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NextAddAsync(stream, _ => true, "something that never comes"));

        // The distinguishing half: silence is NAMED, not rendered as an empty collection.
        Assert.Contains("discarded NOTHING", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("discarded 0", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A stream that yields exactly what it was given and then ends.</summary>
    private sealed class ScriptedDeltaStream(IReadOnlyList<Delta> deltas) : IAsyncStreamReader<Delta>
    {
        private int _index = -1;

        public Delta Current => deltas[_index];

        public Task<bool> MoveNext(CancellationToken cancellationToken) =>
            Task.FromResult(++_index < deltas.Count);
    }

    /// <summary>
    /// Reads the stream until an Add matches; the call's own deadline is the timeout.
    /// </summary>
    /// <remarks>
    /// <b>It names what it DISCARDED when it gives up, and that is the method's purpose rather
    /// than a convenience.</b> The loop drops three different things silently: a delta that is
    /// not an <c>Add</c>, an <c>Add</c> whose payload fails the predicate, and an <c>Add</c>
    /// carrying no <c>content</c> element at all - because <see cref="ContentOf"/> answers empty
    /// for that, and empty fails every predicate here.
    /// <para>
    /// <b>So "nothing arrived" and "something arrived carrying the wrong thing" produced the
    /// identical observation</b> - an <c>RpcException/DeadlineExceeded</c> and nothing else - for
    /// every one of the four recorded failures above. Those two want opposite repairs: one points
    /// at whatever should have sent a delta, the other at what it sent. An empty discard list says
    /// the stream was silent; a populated one says the answer came and was thrown away. Until this
    /// existed each occurrence was a tally mark rather than evidence, which is why four of them
    /// bought no mechanism.
    /// </para>
    /// </remarks>
    private static async Task<Add> NextAddAsync(
        IAsyncStreamReader<Delta> stream,
        Func<Add, bool> matches,
        string awaiting = "an Add matching the predicate")
    {
        var discarded = new List<string>();
        try
        {
            while (await stream.MoveNext(TestContext.Current.CancellationToken))
            {
                var delta = stream.Current;
                if (delta.ActionCase == Delta.ActionOneofCase.Add && matches(delta.Add))
                {
                    return delta.Add;
                }

                discarded.Add(DescribeDiscarded(delta));
            }
        }
        catch (RpcException exception)
        {
            // The deadline arrives HERE rather than ending the loop, so the report has to be
            // written on the way out of MoveNext. Wrapping keeps the original as InnerException.
            throw new InvalidOperationException(
                $"Waiting for {awaiting}: the call ended as {exception.StatusCode}, {DiscardReport(discarded)}",
                exception);
        }

        throw new InvalidOperationException(
            $"Waiting for {awaiting}: the stream ended, {DiscardReport(discarded)}");
    }

    /// <summary>
    /// The discard list as a sentence. The empty case is spelled out rather than shown as an empty
    /// list, because it is the informative half and the one a reader skims past.
    /// </summary>
    private static string DiscardReport(List<string> discarded) =>
        discarded.Count == 0
            ? "having discarded NOTHING - no message reached this reader at all, so the stream was silent rather than carrying the wrong thing."
            : $"having discarded {discarded.Count} message(s): {string.Join(" | ", discarded)}";

    /// <summary>What a discarded delta was, in enough detail to tell the three drop cases apart.</summary>
    private static string DescribeDiscarded(Delta delta)
    {
        if (delta.ActionCase != Delta.ActionOneofCase.Add)
        {
            return delta.ActionCase.ToString();
        }

        var ids = string.Join(",", delta.Add.Elements.Select(element => element.Id?.Value ?? "<no id>"));
        var content = ContentOf(delta.Add);
        return $"Add(ids=[{ids}], content={(content.Length == 0 ? "<no content element>" : $"{content.Length} chars")})";
    }

    /// <summary>Opens the stream and returns the first Add delta the baseline produces.</summary>
    private static async Task<Add> FirstAddDeltaAsync(
        DiagramService.DiagramServiceClient diagramClient,
        Metadata headers,
        Common.Wire.ShortGuid projectId,
        string fileName)
    {
        var path = new Path();
        path.Segments.Add(fileName);
        using var call = diagramClient.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = path },
            headers,
            deadline: DateTime.UtcNow.AddSeconds(30));

        while (await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken))
        {
            if (call.ResponseStream.Current.ActionCase == Delta.ActionOneofCase.Add)
            {
                return call.ResponseStream.Current.Add;
            }
        }

        throw new InvalidOperationException($"The stream for '{fileName}' ended without an Add delta.");
    }

    private GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static async Task<Metadata> LoginAsync(GrpcChannel channel)
    {
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var response = await authClient.LoginAsync(
            new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential },
            cancellationToken: TestContext.Current.CancellationToken);
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<Common.Wire.ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(
            new AddProjectRequest { Path = pathMessage },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }
}
