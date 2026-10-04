using System.Text;
using System.Text.Json;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport;
using Google.Protobuf;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// <b>Every shipped example, exported as the stream a canvas receives when it opens it</b>
/// (client-centralization task 12, Requirement 11.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the backend writes it.</b> The client guard that finds a stylesheet rule nothing emits must
/// mount each canvas on a real model: <c>pipeline-problem-{payload.problemSeverity}</c> is composed from
/// a payload, and the hand-written models the canvas tests build never compose it, so over those the
/// guard would call it dead. Only the backend can turn a shipped example into that payload, so it does,
/// and the client reads what it wrote from <c>src/fixtures/cross-tier/example-models/</c> - the family's
/// cross-tier folder, which keeps the client out of <c>src/backend</c>.
/// </para>
/// <para>
/// <b>Exactly the wire's bytes.</b> Each diagram is routed by <see cref="DiagramFileRouter"/>, opened by
/// its registered <see cref="IDiagramSessionFactory"/> in the real composed host, and its baseline mapped
/// by <see cref="DiagramWire"/> - the mapping <c>DiagramService.Open</c> streams through. Each delta is
/// kept as its protobuf bytes in base64, so the client decodes it with the same generated schema and runs
/// each module's own <c>applyDelta</c> on it unchanged. A pipeline is exported twice, as opened and fully
/// opened through the hook <see cref="DrawnConnectionsTests"/> already uses, because its jobs and steps
/// are drawn only in the second and a shut stage's job count only in the first.
/// </para>
/// <para>
/// <b>Checked in, and failing when stale.</b> The file is compared with what this run produced; when
/// they differ, this run's version is written in its place and the test fails, so a changed example,
/// mapper or payload is one re-run and one commit away, and CI refuses a copy nobody regenerated.
/// </para>
/// </remarks>
public class ShippedExampleModelsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Reason =
        "Written by ShippedExampleModelsTests from src/examples/diagrams (showcase-*) and src/diagrams/*/examples " +
        "(module-*, less any diagram the showcase already holds): each diagram's baseline, fully " +
        "opened, as the base64 protobuf Delta messages DiagramService.Open streams to a canvas. The client's " +
        "noStylesheetRuleWithoutAnEmitter guard mounts every canvas on these (client-centralization R11.2). " +
        "Do not edit by hand: re-run the test and commit what it writes.";

    private readonly WebApplicationFactory<Program> _factory;

    private readonly string _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));

    public ShippedExampleModelsTests(WebApplicationFactory<Program> baseFactory) =>
        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                // As DrawnConnectionsTests: the problem cache stays out of the real user profile.
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<DiagramValidators>()));
            });
        });

    /// <summary>The modules whose canvas draws more once a view is opened, by module or showcase folder name.</summary>
    private static readonly Dictionary<string, Action<IServiceProvider, DrawnView>> Expansions = new(StringComparer.Ordinal)
    {
        ["azure-devops-pipeline"] = DrawnConnectionsTests.ExpandPipelineViews,
    };

    [Fact]
    public async Task EveryShippedExample_IsExportedAsTheModelItsCanvasReceives()
    {
        // Arrange.
        var services = _factory.Services;
        var target = IoPath.Combine(RepositoryRoot(), "src", "fixtures", "cross-tier", "example-models");
        // Both trees a user can open: the showcase, and each module's own examples. A diagram the two
        // share is exported once, from the showcase, which is enumerated first.
        var sources = Directory.EnumerateDirectories(IoPath.Combine(DrawnConnections.ExamplesRoot(), "diagrams"))
            .Order(StringComparer.Ordinal)
            .Select(folder => (File: "showcase-" + IoPath.GetFileName(folder), Module: IoPath.GetFileName(folder), Folder: folder))
            .Concat(Directory.EnumerateDirectories(IoPath.Combine(RepositoryRoot(), "src", "diagrams"))
                .Order(StringComparer.Ordinal)
                .Where(module => Directory.Exists(IoPath.Combine(module, "examples")))
                .Select(module => (File: "module-" + IoPath.GetFileName(module), Module: IoPath.GetFileName(module), Folder: IoPath.Combine(module, "examples"))))
            .ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stale = new List<string>();

        // Act.
        foreach ((string file, string module, string folder) in sources)
        {
            var exported = await ExportAsync(services, folder, module, seen);
            var path = IoPath.Combine(target, file + ".json");
            var existing = File.Exists(path) ? (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n") : null;
            if (existing != exported)
            {
                Directory.CreateDirectory(target);
                await File.WriteAllTextAsync(path, exported.ReplaceLineEndings("\r\n"), new UTF8Encoding(false), TestContext.Current.CancellationToken);
                stale.Add(file);
            }
        }
        var orphans = Directory.Exists(target)
            ? Directory.EnumerateFiles(target, "*.json")
                .Select(IoPath.GetFileNameWithoutExtension)
                .Except(sources.Select(source => source.File), StringComparer.Ordinal)
                .ToList()
            : [];

        // Assert.
        Assert.True(stale.Count == 0,
            $"These exported example models were stale and have been rewritten - commit them: {string.Join(", ", stale)}.");
        Assert.True(orphans.Count == 0,
            $"These exported example models have no examples folder any more - delete them: {string.Join(", ", orphans)}.");
    }

    private static async Task<string> ExportAsync(IServiceProvider services, string source, string name, HashSet<string> seen)
    {
        // Twice, each from its own copy so no store can hand the second its first parse: whatever differs
        // between the two is minted per open, and is replaced below by a token that does not.
        var first = await OpenAllAsync(services, source, name);
        var second = await OpenAllAsync(services, source, name);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            json.WriteStartObject();
            json.WriteString("reason", Reason);
            json.WriteStartArray("diagrams");
            for (var index = 0; index < first.Count; index++)
            {
                (string example, string mimeType, List<byte[]> deltas) = Stabilised(name, first[index], second[index]);
                var encoded = deltas.Select(Convert.ToBase64String).ToList();
                if (!seen.Add(mimeType + "\n" + string.Join("\n", encoded)))
                {
                    continue;
                }
                json.WriteStartObject();
                json.WriteString("example", example);
                json.WriteString("mimeType", mimeType);
                json.WriteStartArray("deltas");
                foreach (var delta in encoded)
                {
                    json.WriteStringValue(delta);
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>
    /// The one diagram's deltas with every element id that differed between the two opens replaced, in
    /// place and at the same length, by a token numbered in order of appearance.
    /// </summary>
    /// <remarks>
    /// Same length, so every length prefix around the id - the element's, its delta's, and the payload's
    /// when a connection names its ends by id - stays true and the bytes still decode. The replacement is
    /// checked rather than trusted: both opens, each with its own ids replaced, must come out identical,
    /// so anything else minted per open (a time, a path) fails here by name rather than churning the file.
    /// </remarks>
    private static (string Example, string MimeType, List<byte[]> Deltas) Stabilised(string name, Opened first, Opened second)
    {
        Assert.Equal(first.Example, second.Example);
        Assert.True(first.Ids.Count == second.Ids.Count, $"{name}: {first.Example} drew {first.Ids.Count} elements on one open and {second.Ids.Count} on the next.");

        var tokens = new List<(string First, string Second, string Token)>();
        for (var index = 0; index < first.Ids.Count; index++)
        {
            (string one, string other) = (first.Ids[index], second.Ids[index]);
            if (one == other || tokens.Exists(token => token.First == one))
            {
                continue;
            }
            Assert.True(one.Length == other.Length, $"{name}: {first.Example} minted ids of different lengths, {one} and {other}, which cannot be replaced in place.");
            var token = ("minted" + tokens.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).PadRight(one.Length, '_');
            Assert.True(token.Length == one.Length, $"{name}: {first.Example} minted {one}, too short for a replacement token.");
            tokens.Add((one, other, token));
        }

        var fromFirst = first.Deltas.Select(delta => Replaced(delta, tokens.Select(token => (token.First, token.Token)))).ToList();
        var fromSecond = second.Deltas.Select(delta => Replaced(delta, tokens.Select(token => (token.Second, token.Token)))).ToList();
        Assert.True(fromFirst.Count == fromSecond.Count && fromFirst.Zip(fromSecond).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)),
            $"{name}: {first.Example} differs between two opens in more than its element ids, so its export cannot be checked in.");
        return (first.Example, first.MimeType, fromFirst);
    }

    private static byte[] Replaced(byte[] delta, IEnumerable<(string From, string To)> replacements)
    {
        var bytes = (byte[])delta.Clone();
        foreach ((string from, string to) in replacements)
        {
            var pattern = Encoding.UTF8.GetBytes(from);
            var replacement = Encoding.UTF8.GetBytes(to);
            for (var at = bytes.AsSpan().IndexOf(pattern); at >= 0;)
            {
                replacement.CopyTo(bytes, at);
                var next = bytes.AsSpan(at + pattern.Length).IndexOf(pattern);
                at = next < 0 ? -1 : at + pattern.Length + next;
            }
        }
        return bytes;
    }

    private sealed record Opened(string Example, string MimeType, List<string> Ids, List<byte[]> Deltas);

    private static async Task<List<Opened>> OpenAllAsync(IServiceProvider services, string source, string name)
    {
        var workspace = IoPath.Combine(IoPath.GetTempPath(), "adp-example-models-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A copy, so opening a diagram can never write into the shipped examples.
            DrawnConnections.CopyTree(source, workspace);
            var router = services.GetRequiredService<DiagramFileRouter>();
            var factories = services.GetServices<IDiagramSessionFactory>().ToList();
            var opened = new List<Opened>();
            foreach ((string example, DiagramRouted routed, IDiagramSessionFactory factory) in DrawnConnections.DiagramsIn(workspace, router, factories))
            {
                var bodyPath = routed.BodyPath ?? routed.RegistrationPath!;
                var watchId = ShortGuid.NewShortGuid();
                await using var session = factory.Open(watchId, workspace, bodyPath, routed.RegistrationPath);
                var baseline = session.Baseline();
                if (Expansions.TryGetValue(name, out var expand))
                {
                    // As opened too, because some of what a canvas draws exists only while a view is
                    // shut - a collapsed stage's job count.
                    opened.Add(Captured(example, routed, baseline));
                    // To a fixed point, as DrawnConnections does: opening the stages reveals the jobs.
                    for (var round = 0; round < 10; round++)
                    {
                        var before = AddedBy(baseline).Count;
                        expand(services, new DrawnView(watchId, bodyPath, AddedBy(baseline)));
                        baseline = session.Baseline();
                        if (AddedBy(baseline).Count == before)
                        {
                            break;
                        }
                    }
                }
                opened.Add(Captured(Expansions.ContainsKey(name) ? example + " (every view opened)" : example, routed, baseline));
            }
            return opened;
        }
        finally
        {
            TestFolder.TryDelete(workspace);
        }
    }

    private static Opened Captured(string example, DiagramRouted routed, IReadOnlyList<DiagramDelta> baseline) => new(
        example,
        routed.Definition.Origin.MimeType,
        [.. AddedBy(baseline).Select(element => element.Id)],
        [.. baseline.Select(delta => DiagramWire.ToProto(delta).ToByteArray())]);

    private static List<DiagramElement> AddedBy(IReadOnlyList<DiagramDelta> deltas) =>
        [.. deltas.OfType<DiagramAddDelta>().SelectMany(add => add.Elements)];

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "fixtures", "cross-tier")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (src/fixtures/cross-tier) was not found above the test binary.");
    }
}
