using EtAlii.Adp.Files;
using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// Load-time validation of a mind map (FBL 17.4, mindmap.md 3.8, etalii.adp spec 092, T016): the central topic's adp-id where
/// it has none, and each linked node's adp-link and cached name where resolution finds them changed, held in memory on
/// opening, never written then, and written with the first save.
/// </summary>
public sealed class MindmapOpeningTests : IDisposable
{
    private const string Opened = "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"Shop relaunch\" ID=\"ID_1\">\n<node TEXT=\"Checkout\" ID=\"ID_2\">\n<attribute NAME=\"adp-link\" VALUE=\"$k7q2m9xa@features.yaml#checkout.Name=Checkout\"/>\n</node>\n<node TEXT=\"Ask the support team\" ID=\"ID_5\"/>\n</node>\n</map>\n";

    private readonly TestFolder _folder = TestFolder.Create("mindmap-opening");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task OpeningAMap_WritesNothing_AndHoldsItsDocumentIdAndItsRefreshedLinksInMemory()
    {
        // Arrange: the feature the node links to was renamed in its own file since the link was written.
        var path = Write(Opened);
        var store = new MindmapDocumentStore(new FileWriter(), new MindmapOpeningIndex(Workspace("Pay")));

        // Act.
        var document = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(Opened, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Matches("^[0-9a-z]{8}$", document.Root.AdpId);
        var linked = Assert.Single(document.Nodes, node => node.AdpLink is not null);
        Assert.Equal("$k7q2m9xa@features.yaml#checkout.Name=Pay", linked.AdpLink);
        Assert.Equal("Pay", linked.Text);
    }

    [Fact]
    public async Task TheFirstSave_WritesWhatOpeningFound()
    {
        // Arrange.
        var path = Write(Opened);
        var store = new MindmapDocumentStore(new FileWriter(), new MindmapOpeningIndex(Workspace("Pay")));
        var document = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        var other = document.Find("ID_5")!;
        document.SetText(other, "Ask support");

        // Act.
        var result = await store.SaveAsync(path, document, new MindmapNodeUpdated("ID_5"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Failed);
        var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Matches("^<map version=\"freeplane 1.11.5\">\n<node TEXT=\"Shop relaunch\" ID=\"ID_1\">\n<attribute NAME=\"adp-id\" VALUE=\"[0-9a-z]{8}\"/>\n<node TEXT=\"Pay\" ID=\"ID_2\">\n<attribute NAME=\"adp-link\" VALUE=\"\\$k7q2m9xa@features.yaml#checkout.Name=Pay\"/>\n</node>\n<node TEXT=\"Ask support\" ID=\"ID_5\"/>\n</node>\n</map>\n$", written);
    }

    [Fact]
    public async Task AMapAsResolutionFindsIt_IsWrittenBackUnchanged()
    {
        // Arrange.
        var text = Opened.Replace("ID=\"ID_1\">\n", "ID=\"ID_1\">\n<attribute NAME=\"adp-id\" VALUE=\"p4r8t1zc\"/>\n", StringComparison.Ordinal);
        var path = Write(text);
        var store = new MindmapDocumentStore(new FileWriter(), new MindmapOpeningIndex(Workspace("Checkout")));

        // Act.
        var document = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(text, document.ToText());
    }

    private string Write(string text)
    {
        var path = _folder.Combine("ideas.mm");
        File.WriteAllText(path, text);
        return path;
    }

    private LinkWorkspace Workspace(string featureName) => new(_folder.Path.Replace('\\', '/'),
    [
        new IndexedDocument(_folder.Combine("features.yaml").Replace('\\', '/'), "k7q2m9xa", "Name", [new IndexedElement("checkout", [new KeyValuePair<string, string>("Name", featureName)])], []),
    ]);
}

/// <summary>An index that holds one workspace for every path, and records the documents it is asked to give new ids.</summary>
internal sealed class MindmapOpeningIndex(LinkWorkspace workspace) : ILinkIndex
{
    public IDisposable Track(string rootPath) => throw new NotSupportedException();

    public LinkWorkspace WorkspaceOf(string path) => workspace;

    /// <summary>The paths given new document ids, in order.</summary>
    public List<string> Renewed { get; } = [];

    public Task<string?> RenewAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        Renewed.AddRange(paths);
        return Task.FromResult<string?>(null);
    }
}
