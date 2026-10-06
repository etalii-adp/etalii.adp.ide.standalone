using System.Text;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Fbl.Tests.RealFiles;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.History;

/// <summary>Undo, redo, snapshots, drift (FBL §7.1, §7.2) and the atomic save (FBL §6.6).</summary>
public class HistoryTests
{
    private const string Timeline = "elements:\n  - id: a\n    label: Alpha\n    start: 2026-01-01\n";

    private static OpenBody Open(string text) =>
        OpenBody.Open(Encoding.UTF8.GetBytes(text), RealFileCorpus.Binding("timeline.fbl", "timeline"), new FblOptions { FileName = "plan.tml" });

    [Fact]
    public void ASnapshotUndoEqualsAnInverseSpliceUndo()
    {
        // Arrange.
        var bytes = Encoding.UTF8.GetBytes(Timeline);
        var edit = Edit.Empty with { Splices = [new Splice(SpliceOperation.ReplaceValue, 31, 36, "\"Al: pha\"")] };
        var inverse = OpenRegistration.Open(bytes);
        var snapshot = OpenRegistration.Open(bytes);

        // Act.
        inverse.Apply(edit);
        snapshot.Apply(edit with { Snapshot = true });
        inverse.Undo();
        snapshot.Undo();

        // Assert.
        Assert.Equal(bytes, inverse.Bytes);
        Assert.Equal(inverse.Bytes, snapshot.Bytes);
    }

    [Fact]
    public void RedoRepeatsTheEditAndIsRefusedOnDrift()
    {
        // Arrange.
        var body = Open(Timeline);
        body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "Beta" }));
        var edited = body.Bytes;
        body.Undo();

        // Act.
        var drifted = body.Redo("other"u8.ToArray());
        var redone = body.Redo();

        // Assert.
        Assert.Equal(SplicedFile.DriftRedo, Assert.IsType<UndoResult.Refused>(drifted).Reason);
        Assert.IsType<UndoResult.Done>(redone);
        Assert.Equal(edited, body.Bytes);
    }

    [Fact]
    public void AnEditToAFork_LeavesTheBodyItWasForkedFrom_AsItWas()
    {
        // Arrange.
        var body = Open(Timeline);
        var fork = body.Fork();

        // Act.
        var result = fork.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "Beta" }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal(Encoding.UTF8.GetBytes(Timeline), body.Bytes);
        Assert.Equal("Alpha", body.Model.Elements.Single().Attributes["label"]);
        Assert.Equal("Beta", fork.Model.Elements.Single().Attributes["label"]);
        Assert.False(body.CanUndo);
        Assert.True(fork.CanUndo);
    }

    [Fact]
    public void AReloadClearsTheHistory()
    {
        // Arrange.
        var body = Open(Timeline);
        body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "Beta" }));

        // Act.
        body.Reload(Encoding.UTF8.GetBytes(Timeline));

        // Assert.
        Assert.False(body.CanUndo);
        Assert.Equal("Alpha", body.Model.Find("a")!.Attributes["label"]);
    }

    [Fact]
    public void ASaveHandsTheHostsWriterTheEditedBytes()
    {
        // Arrange.
        var body = Open(Timeline);
        body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "Beta" }));
        var written = new List<byte[]>();

        // Act.
        body.Save(written.Add);

        // Assert: the atomic write itself is the host's (AdpFileWriter.Save here), not the library's.
        Assert.Equal(body.Bytes, Assert.Single(written));
    }

    [Fact]
    public void AnUnreadableBodyIsNeverSaved()
    {
        // Arrange.
        var body = Open("elements: [unclosed\n");
        var written = new List<byte[]>();

        // Act.
        var refused = Record.Exception(() => body.Save(written.Add));

        // Assert.
        Assert.IsType<InvalidOperationException>(refused);
        Assert.True(body.IsReadOnly);
        Assert.Empty(written);
    }
}
