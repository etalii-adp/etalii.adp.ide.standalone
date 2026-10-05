using System.Text;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Tests.RealFiles;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Plugins;

/// <summary>
/// The changes only a persistence plugin can write (runtime plan step S16): a move to another parent
/// and index, a type change, and an add at a position among its siblings. A plugin body hands each to
/// its plugin as it was made; a declared binding refuses each with a sentence, writing nothing.
/// </summary>
public class PluginOnlyChangesTests
{
    private const string TurtleId = "net.etalii.adp.w3c.turtle";

    private const string Timeline = "elements:\n  - id: a\n    label: Alpha\n    start: 2026-01-01\n";

    public static TheoryData<string> Changes() => ["move", "retype", "add at an index"];

    [Theory]
    [MemberData(nameof(Changes))]
    public void APluginBody_HandsTheChangeToItsPlugin_AsItWasMade(string name)
    {
        // Arrange.
        var plugin = new RecordingPlugin(TurtleId);
        var body = PluginBody.Open("label a\n"u8.ToArray(), RealFileCorpus.Binding("w3c-turtle.fbl", "turtle"), plugin);
        var change = ChangeOf(name);

        // Act.
        var result = body.Plan(change);

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Same(change, plugin.Received);
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public void ADeclaredBinding_RefusesTheChange_AndWritesNothing(string name)
    {
        // Arrange.
        var original = Encoding.UTF8.GetBytes(Timeline);
        var body = OpenBody.Open(original, RealFileCorpus.Binding("timeline.fbl", "timeline"), new FblOptions { FileName = "plan.tml" });

        // Act.
        var result = body.Change(ChangeOf(name));

        // Assert.
        var refused = Assert.IsType<PlanResult.Refused>(result);
        Assert.Contains("only a persistence plugin can", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(original, body.Bytes);
    }

    [Fact]
    public void ADeclaredBinding_StillAddsWithoutAnIndex()
    {
        // Arrange.
        var body = OpenBody.Open(Encoding.UTF8.GetBytes(Timeline), RealFileCorpus.Binding("timeline.fbl", "timeline"), new FblOptions { FileName = "plan.tml" });

        // Act.
        var result = body.Change(new ModelChange.Add("Moment", "b", new Dictionary<string, object?> { ["label"] = "Beta", ["start"] = "2026-02-01" }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Contains("Beta", Encoding.UTF8.GetString(body.Bytes), StringComparison.Ordinal);
    }

    private static ModelChange ChangeOf(string name) => name switch
    {
        "move" => new ModelChange.Move("a", null, 0),
        "retype" => new ModelChange.Retype("a", "Period", new Dictionary<string, object?> { ["end"] = "2026-02-01" }),
        _ => new ModelChange.Add("Moment", "b", new Dictionary<string, object?> { ["label"] = "Beta", ["start"] = "2026-02-01" }, null, 0),
    };

    /// <summary>A plugin that records the change it is asked to plan, and plans it as no splice.</summary>
    private sealed class RecordingPlugin(string id) : IPersistencePlugin
    {
        public string Id { get; } = id;

        public ModelChange? Received { get; private set; }

        public PluginReadResult Read(PluginReadRequest request) => new([], [], false);

        public PluginPlanResult Plan(PluginPlanRequest request)
        {
            Received = request.Change;
            return new PluginPlanResult.Planned([]);
        }

        public byte[] Template(PluginTemplateRequest request) => [];

        public IReadOnlyList<string> Watch(PluginReadResult last) => [];
    }
}
