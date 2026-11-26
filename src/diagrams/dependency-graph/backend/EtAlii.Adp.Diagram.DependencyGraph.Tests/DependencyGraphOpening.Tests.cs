using EtAlii.Adp.Files;
using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// Load-time validation of a dependency graph (FBL 17.4, etalii.adp spec 092, T016): a document id where it has none and each
/// link whose parts differ from what resolution finds are held pending on opening and never written then.
/// </summary>
public sealed class DependencyGraphOpeningTests : IDisposable
{
    private const string Opened = "dependencies: 1\nelements:\n  - id: checkout\n    link: $k7q2m9xa@features.yaml#checkout.Name=Checkout\n    x: 0\n    row: 2\n";

    private readonly TestFolder _folder = TestFolder.Create("graph-opening");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task OpeningAGraph_WritesNothing_AndHoldsItsDocumentIdAndItsRefreshedLinksPending()
    {
        // Arrange: the feature the link names was renamed in its own file since the link was written.
        var path = _folder.Combine("services.dgr");
        await File.WriteAllTextAsync(path, Opened, TestContext.Current.CancellationToken);
        var workspace = new LinkWorkspace(_folder.Path.Replace('\\', '/'),
        [
            new IndexedDocument(_folder.Combine("features.yaml").Replace('\\', '/'), "k7q2m9xa", "Name", [new IndexedElement("checkout", [new KeyValuePair<string, string>("Name", "Pay")])], []),
        ]);
        var store = new DependencyGraphDocumentStore(new FileWriter(), new DependencyGraphOpeningIndex(workspace));

        // Act.
        var entry = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(Opened, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.NotNull(entry.Pending);
        Assert.Matches("^dependencies: 1\nid: [0-9a-z]{8}\nelements:\n", entry.Pending);
        Assert.Contains("    link: $k7q2m9xa@features.yaml#checkout.Name=Pay\n", entry.Pending, StringComparison.Ordinal);
    }
}

/// <summary>An index that holds one workspace for every path, and records the documents it is asked to give new ids.</summary>
internal sealed class DependencyGraphOpeningIndex(LinkWorkspace workspace) : ILinkIndex
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

    public event EventHandler<LinkIndexChangedEventArgs>? Changed;

    /// <summary>Raises <see cref="Changed"/> for the document at <paramref name="path"/>, as the index does once it read it again.</summary>
    public void Raise(string path) => Changed?.Invoke(this, new LinkIndexChangedEventArgs(path));
}
