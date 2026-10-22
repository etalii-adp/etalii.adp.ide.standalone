using System.Text;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.History;

/// <summary>
/// Drift before an edit (FBL 0.7 §7.2): an edit planned against bytes that are no longer the body's is
/// refused with the library's one sentence, nothing is written, and the body is read again.
/// </summary>
public class OpenBodyDriftTests
{
    private const string Timeline = "elements:\n  - id: a\n    label: Alpha\n    start: 2026-01-01\n";

    private static OpenBody Open(string text) =>
        OpenBody.Open(Encoding.UTF8.GetBytes(text), RealFileCorpus.Binding("timeline.fbl", "timeline"), new FblOptions { FileName = "plan.tml" });

    [Fact]
    public void AnEditPlannedAfterTheBodyChangedOnDisk_IsRefused_AndWritesNothing()
    {
        // Arrange: the body as read, and the same file after another writer changed it.
        var body = Open(Timeline);
        var read = body.Bytes;
        var onDisk = Encoding.UTF8.GetBytes(Timeline.Replace("Alpha", "Gamma", StringComparison.Ordinal));

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "Beta" }), onDisk);

        // Assert: refused, nothing of the edit applied, and the body read again as it is on disk (FBL 0.7 7.2, 7.3).
        Assert.Equal(SplicedFile.DriftEdit, Assert.IsType<PlanResult.Refused>(result).Reason);
        Assert.NotEqual(read, body.Bytes);
        Assert.Equal(onDisk, body.Bytes);
        Assert.DoesNotContain("Beta", Encoding.UTF8.GetString(body.Bytes), StringComparison.Ordinal);
        Assert.False(body.CanUndo);
    }

    [Fact]
    public void AnEditPlannedAgainstTheBytesOnDisk_IsWritten()
    {
        // Arrange.
        var body = Open(Timeline);

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "Beta" }), body.Bytes.ToArray());

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Contains("Beta", Encoding.UTF8.GetString(body.Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSentence_IsTheOneFbl72Gives()
    {
        Assert.Equal("The file has changed since this edit was made, so it was not written.", SplicedFile.DriftEdit);
    }
}
