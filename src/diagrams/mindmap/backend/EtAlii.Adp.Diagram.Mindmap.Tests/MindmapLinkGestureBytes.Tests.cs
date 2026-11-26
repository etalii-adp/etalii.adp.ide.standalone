using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The bytes a mind map holds after a link gesture (etalii.adp spec 092, T024, SC-003): a linked node added from the lookup as the
/// central topic's child, saved and read again. <see cref="Expected"/> is what the desktop host writes for the same gesture on the
/// same map, the new node's id aside, which each host draws for itself.
/// </summary>
public sealed partial class MindmapLinkGestureBytesTests : IDisposable
{
    private const string Plain = "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"Shop relaunch\" ID=\"ID_1\">\n<attribute NAME=\"adp-id\" VALUE=\"p4r8t1zc\"/>\n</node>\n</map>\n";

    /// <summary>The map after the gesture, the new node's id written as <c>{node}</c>.</summary>
    private const string Expected = "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"Shop relaunch\" ID=\"ID_1\">\n<attribute NAME=\"adp-id\" VALUE=\"p4r8t1zc\"/>\n<node TEXT=\"Launch\" ID=\"{node}\">\n<attribute NAME=\"adp-link\" VALUE=\"$r6d3w8ne@roadmap.tml#launch=Launch\"/>\n</node>\n</node>\n</map>\n";

    private readonly TestFolder _folder = TestFolder.Create("adp-mindmap-link-gestures");
    private readonly ServiceProvider _services = new ServiceCollection().AddCommands().AddMindmapCommands().BuildServiceProvider();

    public void Dispose()
    {
        _services.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task ALinkedNode_IsWrittenAsTheDesktopHostWritesIt_AndReadsBackLinked()
    {
        // Arrange.
        var path = IoPath.Combine(_folder.Path, "ideas.mm");
        await File.WriteAllTextAsync(path, Plain, TestContext.Current.CancellationToken);
        var actions = Actions();

        // Act: add the linked node, then read the map again from its bytes, through a store that has not seen it.
        var added = await actions.CommitAsync(Target(path, "ID_1"), MindmapContextActionProvider.AddLinkedActionId, "$r6d3w8ne@roadmap.tml#launch=Launch", "", TestContext.Current.CancellationToken);
        var written = await ReadAsync(path);
        var node = NodeId().Match(written).Groups[1].Value;
        using var fresh = new ServiceCollection().AddCommands().AddMindmapCommands().BuildServiceProvider();
        var reopened = new MindmapContextActionProvider(fresh.GetRequiredService<IHistoryStackStore>(), fresh.GetRequiredService<IMindmapDocumentStore>(), new MindmapViewState(), new MindmapOpeningIndex(Workspace()));
        var opened = await reopened.ExecuteAsync(Target(path, node), LinkNavigation.OpenActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(added.Completed, added.Error);
        Assert.Equal(Expected.Replace("{node}", node, StringComparison.Ordinal), written);
        Assert.Equal("launch", Assert.IsType<ContextExecutionReveals>(opened).ElementId);
    }

    [GeneratedRegex("<node TEXT=\"Launch\" ID=\"([^\"]+)\">")]
    private static partial Regex NodeId();

    private MindmapContextActionProvider Actions() => new(
        _services.GetRequiredService<IHistoryStackStore>(), _services.GetRequiredService<IMindmapDocumentStore>(), new MindmapViewState(), new MindmapOpeningIndex(Workspace()));

    private ContextTarget Target(string path, string elementId) => ContextTarget.ForElement(path, elementId, ProjectRoot.From(_folder.Path), ShortGuid.NewShortGuid());

    private static async Task<string> ReadAsync(string path) => (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

    private LinkWorkspace Workspace() => new(_folder.Path.Replace('\\', '/'), [
        new IndexedDocument(_folder.Path.Replace('\\', '/') + "/roadmap.tml", "r6d3w8ne", "label", [new IndexedElement("launch", [new KeyValuePair<string, string>("label", "Launch")])], []),
    ]);
}
