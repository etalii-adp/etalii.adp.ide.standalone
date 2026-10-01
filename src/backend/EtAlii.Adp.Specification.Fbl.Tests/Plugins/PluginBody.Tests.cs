using System.Text;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Tests.RealFiles;
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
