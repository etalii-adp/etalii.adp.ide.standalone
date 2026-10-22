using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Plugins;

/// <summary>
/// What a tool asks of a plugin body beyond one change at a time: the sentence it refuses an unreadable body with,
/// one history entry for the changes of one gesture, and the spans of the values a plugin reads.
/// </summary>
public class PluginBodyGesturesTests
{
    private const string TurtleId = "net.etalii.adp.w3c.turtle";

    private static FblBinding Turtle => RealFileCorpus.Binding("w3c-turtle.fbl", "turtle");

    private static ModelChange Label(string id, string label) => new ModelChange.Set(id, new Dictionary<string, object?> { ["label"] = label });

    [Fact]
    public void AnUnreadableBody_IsRefusedWithTheToolsSentence_OrTheLibrarysWhenItWordsNone()
    {
        // Arrange.
        var worded = PluginBody.Open("label a\n"u8.ToArray(), Turtle, new UnreadablePlugin(TurtleId), "a.ttl", "This file does not parse, so nothing can be edited until it is fixed.");
        var plain = PluginBody.Open("label a\n"u8.ToArray(), Turtle, new UnreadablePlugin(TurtleId), "a.ttl");

        // Act.
        var wordedResult = worded.Change(Label("line1", "b"));
        var plainResult = plain.Change(Label("line1", "b"));

        // Assert.
        Assert.Equal("This file does not parse, so nothing can be edited until it is fixed.", Assert.IsType<PlanResult.Refused>(wordedResult).Reason);
        Assert.Equal(PluginBody.Unreadable, Assert.IsType<PlanResult.Refused>(plainResult).Reason);
    }

    [Fact]
    public void TheChangesOfOneGroup_AreOneUndoStep_AndRedoneTogether()
    {
        // Arrange.
        var original = "label a\nlabel b\nlabel c\n"u8.ToArray();
        var body = PluginBody.Open(original, Turtle, new FakePlugin(TurtleId));

        // Act.
        body.Group(() =>
        {
            body.Change(Label("line1", "first"));
            body.Change(Label("line3", "third"));
        });
        var changed = body.Bytes;
        var undone = body.Undo();

        // Assert.
        Assert.Equal("label first\nlabel b\nlabel third\n", Encoding.UTF8.GetString(changed));
        Assert.IsType<UndoResult.Done>(undone);
        Assert.Equal(original, body.Bytes);
        Assert.False(body.CanUndo);
        Assert.IsType<UndoResult.Done>(body.Redo());
        Assert.Equal(changed, body.Bytes);
    }

    [Fact]
    public void WithoutAGroup_EachChangeIsItsOwnUndoStep()
    {
        // Arrange.
        var body = PluginBody.Open("label a\nlabel b\n"u8.ToArray(), Turtle, new FakePlugin(TurtleId));

        // Act.
        body.Change(Label("line1", "first"));
        body.Change(Label("line2", "second"));
        body.Undo();

        // Assert.
        Assert.Equal("label first\nlabel b\n", Encoding.UTF8.GetString(body.Bytes));
        Assert.True(body.CanUndo);
    }

    [Fact]
    public void AGroupsSplice_KeepsWholeCharacters_SoItsTextDecodes()
    {
        // Arrange: é and è share their first byte, so the bytes differ in the middle of a character.
        var original = "label café\n"u8.ToArray();
        var body = PluginBody.Open(original, Turtle, new FakePlugin(TurtleId));

        // Act.
        body.Group(() => body.Change(Label("line1", "cafè")));
        body.Undo();
        var undone = body.Bytes;
        body.Redo();

        // Assert.
        Assert.Equal(original, undone);
        Assert.Equal("label cafè\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AGroupsSplice_EndsOnAWholeCharacter_WhenTwoCharactersShareTheirLastByte()
    {
        // Arrange: é (C3 A9) and ɩ (C9 A9) differ in their first byte and share their second.
        var original = "label café\n"u8.ToArray();
        var body = PluginBody.Open(original, Turtle, new FakePlugin(TurtleId));

        // Act.
        body.Group(() => body.Change(Label("line1", "cafɩ")));
        body.Undo();
        var undone = body.Bytes;
        body.Redo();

        // Assert.
        Assert.Equal(original, undone);
        Assert.Equal("label cafɩ\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AGroupThatChangesNothing_RecordsNothing()
    {
        // Arrange.
        var body = PluginBody.Open("label a\n"u8.ToArray(), Turtle, new FakePlugin(TurtleId));

        // Act.
        body.Group(() => { });

        // Assert.
        Assert.False(body.CanUndo);
    }

    [Fact]
    public void TheSpansOfAPluginsValues_ReachTheModel_AndAValueMayHaveNone()
    {
        // Arrange.
        var body = PluginBody.Open("label a\nlabel b\n"u8.ToArray(), Turtle, new SpanningPlugin(TurtleId));

        // Act.
        var model = body.Model;

        // Assert.
        Assert.Equal(new Span(6, 7), model.Find("line1")!.ValueSpans["label"]);
        Assert.Empty(model.Find("line2")!.ValueSpans);
    }

    /// <summary>A plugin that reports every body unreadable.</summary>
    private sealed class UnreadablePlugin(string id) : IPersistencePlugin
    {
        public string Id { get; } = id;

        public PluginReadResult Read(PluginReadRequest request) => new([], [], true);

        public PluginPlanResult Plan(PluginPlanRequest request) => throw new InvalidOperationException("An unreadable body is never planned.");

        public byte[] Template(PluginTemplateRequest request) => [];

        public IReadOnlyList<string> Watch(PluginReadResult last) => [];
    }

    /// <summary>The fake plugin's reading, with the span of the first line's label as its value span and none for the others.</summary>
    private sealed class SpanningPlugin(string id) : IPersistencePlugin
    {
        private readonly FakePlugin _lines = new(id);

        public string Id { get; } = id;

        public PluginReadResult Read(PluginReadRequest request)
        {
            var read = _lines.Read(request);
            return read with
            {
                Elements = [.. read.Elements.Select(element => element.Id == "line1"
                    ? element with { ValueSpans = new Dictionary<string, Span> { ["label"] = element.OwnSpan with { Start = element.OwnSpan.Start + 6 } } }
                    : element)],
            };
        }

        public PluginPlanResult Plan(PluginPlanRequest request) => _lines.Plan(request);

        public byte[] Template(PluginTemplateRequest request) => [];

        public IReadOnlyList<string> Watch(PluginReadResult last) => [];
    }
}
