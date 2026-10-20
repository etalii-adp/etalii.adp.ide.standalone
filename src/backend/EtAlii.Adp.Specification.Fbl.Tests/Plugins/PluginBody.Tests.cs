using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Plugins;

/// <summary>
/// The host side of the plugin contract (FBL §11.3, §15.1; Requirement 9.1 and 9.3), proved with a
/// hand-written fake plugin, since no plugin is implemented by this library.
/// </summary>
public class PluginBodyTests
{
    private const string TurtleId = "net.etalii.adp.w3c.turtle";

    [Fact]
    public void AMissingPluginOpensTheBodyReadOnlyWithAFinding()
    {
        // Arrange.
        var turtle = RealFileCorpus.Binding("w3c-turtle.fbl", "turtle");

        // Act.
        var body = PluginBody.Open("ex:a ex:b ex:c .\n"u8.ToArray(), turtle, plugin: null, "a.ttl");

        // Assert.
        Assert.True(body.IsReadOnly);
        var finding = Assert.Single(body.Model.Findings);
        Assert.Equal(FindingCodes.PluginMissing, finding.Code);
        Assert.Contains(TurtleId, finding.Message, StringComparison.Ordinal);
        Assert.IsType<PlanResult.Refused>(body.Change(new ModelChange.Remove("x")));
        Assert.Throws<InvalidOperationException>(() => body.Save(_ => Assert.Fail("A read-only body is never written.")));
    }

    [Fact]
    public void APluginWithAnotherIdCountsAsMissing()
    {
        // Act.
        var body = PluginBody.Open("x\n"u8.ToArray(), RealFileCorpus.Binding("w3c-turtle.fbl", "turtle"), new FakePlugin("net.example.other"));

        // Assert.
        Assert.True(body.IsReadOnly);
        Assert.Equal(FindingCodes.PluginMissing, Assert.Single(body.Model.Findings).Code);
    }

    [Fact]
    public void ThePluginsSplicesAreAppliedRecordedAndUndone()
    {
        // Arrange.
        var original = "label a\nlabel b\n"u8.ToArray();
        var body = PluginBody.Open(original, RealFileCorpus.Binding("w3c-turtle.fbl", "turtle"), new FakePlugin(TurtleId));

        // Act.
        var result = body.Change(new ModelChange.Set("line1", new Dictionary<string, object?> { ["label"] = "renamed" }));

        // Assert: the host applied the plugin's splice and read again through the plugin.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("label renamed\nlabel b\n", Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal("renamed", body.Model.Find("line1")!.Attributes["label"]);
        Assert.Equal(SplicedFile.DriftUndo, Assert.IsType<UndoResult.Refused>(body.Undo("drifted"u8.ToArray())).Reason);
        Assert.IsType<UndoResult.Done>(body.Undo());
        Assert.Equal(original, body.Bytes);
    }

    [Fact]
    public void APluginsRefusalWritesNothing()
    {
        // Arrange.
        var original = "label a\n"u8.ToArray();
        var body = PluginBody.Open(original, RealFileCorpus.Binding("w3c-turtle.fbl", "turtle"), new FakePlugin(TurtleId));

        // Act.
        var result = body.Change(new ModelChange.Remove("line1"));

        // Assert.
        Assert.Equal("The fake plugin removes nothing.", Assert.IsType<PlanResult.Refused>(result).Reason);
        Assert.Equal(original, body.Bytes);
    }

    [Fact]
    public void AReadOnlyBindingsPluginIsNeverAskedToPlan()
    {
        // Arrange: the Helm chart binding is read-only.
        var plugin = new FakePlugin("net.etalii.adp.helm.chartFolder");
        var body = PluginBody.Open("label a\n"u8.ToArray(), RealFileCorpus.Binding("helm-chart.fbl", "chart"), plugin);

        // Act.
        var result = body.Change(new ModelChange.Set("line1", new Dictionary<string, object?> { ["label"] = "x" }));

        // Assert.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal(0, plugin.Plans);
    }

    [Fact]
    public void AReadOnlyBindingsPluginIsNeverAskedToPlanAPlacement()
    {
        // Arrange: the Helm chart binding is read-only; a placement is a box moved on the canvas.
        var plugin = new FakePlugin("net.etalii.adp.helm.chartFolder");
        var body = PluginBody.Open("label a\n"u8.ToArray(), RealFileCorpus.Binding("helm-chart.fbl", "chart"), plugin);

        // Act.
        var result = body.Change(new ModelChange.Place("line1", 30, 40));

        // Assert.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal(0, plugin.Plans);
    }

    [Fact]
    public void TheBindingsArgsAreHandedToEveryRead()
    {
        // Arrange.
        var plugin = new RecordingFilePlugin(RecordingId);
        var body = PluginBody.Open("a\n"u8.ToArray(), Binding(""" "args": { "depth": 2, "root": "src" } """), plugin);

        // Act: a change reads the body again.
        body.Change(new ModelChange.Set("a", new Dictionary<string, object?>()));

        // Assert.
        Assert.Equal(2, plugin.Reads.Count);
        Assert.All(plugin.Reads, read => Assert.Equal("""{ "depth": 2, "root": "src" }""", read.Args?.GetRawText()));
    }

    [Fact]
    public void ABindingWithoutArgsHandsReadNone()
    {
        // Arrange.
        var plugin = new RecordingFilePlugin(RecordingId);

        // Act.
        PluginBody.Open("a\n"u8.ToArray(), Binding(), plugin);

        // Assert.
        Assert.Null(Assert.Single(plugin.Reads).Args);
    }

    [Fact]
    public void AReadersArgsThatAreNotAnObjectAreAnError()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(Json(""" "args": ["src"] """)), out var document);

        // Assert.
        Assert.Null(document);
        Assert.Contains(problems, p => p is { Severity: LoadSeverity.Error, Pointer: "/bindings/recorded/reader/args" });
    }

    [Fact]
    public void TheFileOfAnElementInAnotherFileIsKept()
    {
        // Arrange: the second element was read from a file the body leads to.
        var plugin = new RecordingFilePlugin(RecordingId) { OtherFile = "lib/b.txt" };

        // Act.
        var body = PluginBody.Open("a\n"u8.ToArray(), Binding(), plugin);

        // Assert.
        Assert.Equal([("a", null), ("b", "lib/b.txt")], body.Model.Elements.Select(e => (e.Id, e.File)));
    }

    [Fact]
    public void ThePluginsWatchIsAskedAfterEveryReadAndItsPathsAreTheBodysToWatch()
    {
        // Arrange.
        var plugin = new RecordingFilePlugin(RecordingId) { Watched = ["lib/b.txt", "lib"] };
        var body = PluginBody.Open("a\n"u8.ToArray(), Binding(), plugin);
        Assert.Equal(["lib/b.txt", "lib"], body.WatchPaths);

        // Act: the next read leads to another file.
        plugin.Watched = ["lib/c.txt"];
        body.Change(new ModelChange.Set("a", new Dictionary<string, object?>()));

        // Assert: watch was given each read's own result.
        Assert.Equal(["lib/c.txt"], body.WatchPaths);
        Assert.Equal(plugin.Results, plugin.Watches);
        Assert.Equal(2, plugin.Watches.Count);
    }

    [Fact]
    public void WithoutThePluginNothingBeyondTheBodyIsWatched()
    {
        // Act.
        var body = PluginBody.Open("a\n"u8.ToArray(), Binding(), plugin: null);

        // Assert.
        Assert.Empty(body.WatchPaths);
    }

    private const string RecordingId = "net.example.recorded";

    /// <summary>A file binding of no tool type, read by <see cref="RecordingFilePlugin"/>; <paramref name="reader"/> is added to its reader.</summary>
    private static string Json(string reader = "") => $$"""
        { "fbl": "0.2", "bindings": { "recorded": {
          "claims": { "extensions": [".rec"] },
          "body": { "kind": "file" },
          "reader": { "plugin": "{{RecordingId}}", "version": "^0.1.0"{{(reader.Length > 0 ? "," + reader : "")}} }
        } } }
        """;

    private static FblBinding Binding(string reader = "")
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(Json(reader)), out var document);
        Assert.DoesNotContain(problems, p => p.Severity == LoadSeverity.Error);
        return document!.Bindings["recorded"];
    }
}

/// <summary>
/// A plugin that records what the host hands it: each read's request, and each result handed back to
/// <c>watch</c>. It reads one element <c>a</c> from the body and, when <see cref="OtherFile"/> is set,
/// one element <c>b</c> from that file; it plans every change as an <c>x</c> put before the body, so that the body is read again.
/// </summary>
internal sealed class RecordingFilePlugin(string id) : IPersistencePlugin
{
    public string Id { get; } = id;

    public List<PluginReadRequest> Reads { get; } = [];

    public List<PluginReadResult> Results { get; } = [];

    public List<PluginReadResult> Watches { get; } = [];

    public string? OtherFile { get; init; }

    public IReadOnlyList<string> Watched { get; set; } = [];

    public PluginReadResult Read(PluginReadRequest request)
    {
        Reads.Add(request);
        var elements = new List<FblElement> { Element("a", null) };
        if (OtherFile is { } other) elements.Add(Element("b", other));
        var result = new PluginReadResult(elements, [], false);
        Results.Add(result);
        return result;
    }

    public PluginPlanResult Plan(PluginPlanRequest request) => new PluginPlanResult.Planned([new PluginSplice("", new Splice(SpliceOperation.InsertEntry, 0, 0, "x"))]);

    public byte[] Template(PluginTemplateRequest request) => throw new NotSupportedException("No body is created here.");

    public IReadOnlyList<string> Watch(PluginReadResult last)
    {
        Watches.Add(last);
        return Watched;
    }

    private static FblElement Element(string id, string? file) =>
        new(id, true, "Item", "item", false, new Dictionary<string, object?>(), null, null, null, null, new Span(0, 0), 1) { File = file };
}

/// <summary>
/// A fake persistence plugin: every line <c>label &lt;text&gt;</c> is an element <c>line&lt;n&gt;</c>
/// with a writable <c>label</c>; it plans label changes and refuses everything else.
/// </summary>
internal sealed class FakePlugin(string id) : IPersistencePlugin
{
    public string Id { get; } = id;

    public int Plans { get; private set; }

    public PluginReadResult Read(PluginReadRequest request)
    {
        var bytes = request.Files[0].Bytes;
        var elements = new List<FblElement>();
        var start = 0;
        var number = 0;
        for (var i = 0; i <= bytes.Length; i++)
        {
            if (i < bytes.Length && bytes[i] != (byte)'\n') continue;
            if (i > start)
            {
                number++;
                var text = Encoding.UTF8.GetString(bytes, start, i - start);
                elements.Add(new FblElement($"line{number}", true, "Line", "line", false, new Dictionary<string, object?> { ["label"] = text[6..] },
                    null, null, null, null, new Span(start, i), number));
            }
            start = i + 1;
        }
        return new PluginReadResult(elements, [], false);
    }

    public PluginPlanResult Plan(PluginPlanRequest request)
    {
        Plans++;
        if (request.Change is not ModelChange.Set set) return new PluginPlanResult.Refused("The fake plugin removes nothing.");
        var element = request.Last.Find(set.Id)!;
        var start = element.OwnSpan.Start + 6;
        return new PluginPlanResult.Planned([new PluginSplice("", new Splice(SpliceOperation.ReplaceValue, start, element.OwnSpan.End, (string)set.Attributes["label"]!))]);
    }

    public byte[] Template(PluginTemplateRequest request) => Encoding.UTF8.GetBytes($"template for {request.Name} ({request.Placeholders["base"]})");

    public IReadOnlyList<string> Watch(PluginReadResult last) => [];
}

internal static class PluginReadResultExtensions
{
    public static FblElement? Find(this PluginReadResult result, string id) => result.Elements.FirstOrDefault(e => e.Id == id);
}
