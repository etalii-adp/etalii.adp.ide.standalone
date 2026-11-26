using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The bytes a timeline holds after a link gesture (etalii.adp spec 092, T024, SC-003): a linked moment added from the lookup where
/// the menu was opened, saved and read again. <see cref="Expected"/> is what the desktop host writes for the same gesture on the
/// same document, the new id aside, which each host draws for itself.
/// </summary>
public sealed partial class TimelineLinkGestureBytesTests : IDisposable
{
    private const string Plain = "timeline: 1\nid: r6d3w8ne\nelements:\n  - id: launch\n    label: Launch\n    begin: 2027-01-04\n    row: 0\n";

    /// <summary>The document after the gesture, the new id written as <c>{moment}</c>.</summary>
    private const string Expected = Plain + "  - id: {moment}\n    link: $s2v5b9hu@services.dgr#pay=Payments\n    begin: 2027-02-01\n    row: 3\n";

    private readonly TestFolder _folder = TestFolder.Create("adp-timeline-link-gestures");
    private readonly TimelineDocumentStore _store = new(new FileWriter());
    private readonly IHistoryStackStore _historyStacks;

    public TimelineLinkGestureBytesTests()
    {
        _historyStacks = TestHistory.For(s => { s.AddTimeline(); s.AddSingleton<ITimelineDocumentStore>(_store); });
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ALinkedMoment_IsWrittenAsTheDesktopHostWritesIt_AndReadsBackLinked()
    {
        // Arrange.
        var path = IoPath.Combine(_folder.Path, "roadmap.tml");
        await File.WriteAllTextAsync(path, Plain, TestContext.Current.CancellationToken);
        var actions = new TimelineContextActionProvider(_historyStacks, _store, new TimelineOpeningIndex(Workspace()));
        var placement = GestureIds.RowPlacement(TimelineScale.ToSeconds(new DateTimeOffset(2027, 2, 1, 15, 30, 0, TimeSpan.Zero)), 3);

        // Act.
        var added = await actions.CommitAsync(Target(path, placement), TimelineContextActionProvider.AddLinkedActionId, "$s2v5b9hu@services.dgr#pay=Payments", "", TestContext.Current.CancellationToken);
        var written = await ReadAsync(path);
        var moment = MomentId().Match(written).Groups[1].Value;
        _store.Forget(path);
        var opened = await actions.ExecuteAsync(Target(path, moment), LinkNavigation.OpenActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(added.Completed, added.Error);
        Assert.Equal(Expected.Replace("{moment}", moment, StringComparison.Ordinal), written);
        Assert.Equal("pay", Assert.IsType<ContextExecutionReveals>(opened).ElementId);
    }

    [GeneratedRegex("  - id: ([^\\n]+)\\n    link: ")]
    private static partial Regex MomentId();

    private ContextTarget Target(string path, string elementId) => ContextTarget.ForElement(path, elementId, ProjectRoot.From(_folder.Path), ShortGuid.NewShortGuid());

    private static async Task<string> ReadAsync(string path) => (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

    private LinkWorkspace Workspace() => new(_folder.Path.Replace('\\', '/'), [
        new IndexedDocument(_folder.Path.Replace('\\', '/') + "/services.dgr", "s2v5b9hu", "label", [new IndexedElement("pay", [new KeyValuePair<string, string>("label", "Payments")])], []),
    ]);
}
