using EtAlii.Adp.Files;
using Xunit;

namespace EtAlii.Adp.Links.Tests;

/// <summary>
/// Giving documents new document ids, as the fix of <c>link.ambiguous-document</c> does to every candidate but the chosen one
/// (FBL 17.6, etalii.adp spec 092, T017): written to the file, and heard back by the index, after which the link resolves.
/// </summary>
public sealed class LinkIndexRenewTests : IDisposable
{
    private const string Link = "$r1@release.lnk.txt#m1=Launch";

    private readonly TestFolder _folder = TestFolder.Create("link-index-renew");
    private readonly ProjectFileEvents _events = new();

    public void Dispose()
    {
        _events.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task ACopyGivenANewId_LeavesTheLinkResolvingToTheOriginal()
    {
        // Arrange: a copy beside the original carries its id and name, so the link is ambiguous.
        Write("release.lnk.txt", "id: r1", "element: m1 Launch");
        Write(Path.Combine("copy", "release.lnk.txt"), "id: r1", "element: m1 Launch");
        using var index = new LinkIndex(_events, [new LinkIndexTestIndexer()], new FileWriter());
        using var tracked = index.Track(_folder.Path);
        Assert.Equal("link.ambiguous-document", index.WorkspaceOf(Held("release.lnk.txt"))!.Resolve(Link).Finding?.Code);

        // Act.
        var refusal = await index.RenewAsync([Held("copy", "release.lnk.txt")], TestContext.Current.CancellationToken);

        // Assert.
        Assert.Null(refusal);
        Assert.StartsWith("id: renewed", await File.ReadAllTextAsync(_folder.Combine("copy", "release.lnk.txt"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.True(await Eventually.TrueAsync(() => index.WorkspaceOf(Held("release.lnk.txt"))!.Resolve(Link).Document?.Path == Held("release.lnk.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADocumentThatCannotBeGivenANewId_IsRefused_AndLeftAsItIs()
    {
        // Arrange.
        Write("release.lnk.txt", "id: r1", "fixed", "element: m1 Launch");
        using var index = new LinkIndex(_events, [new LinkIndexTestIndexer()], new FileWriter());

        // Act.
        var refusal = await index.RenewAsync([Held("release.lnk.txt")], TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("release.lnk.txt could not be given a new document id.", refusal);
        Assert.StartsWith("id: r1", await File.ReadAllTextAsync(_folder.Combine("release.lnk.txt"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAWriter_NothingIsRenewed()
    {
        // Arrange.
        Write("release.lnk.txt", "id: r1", "element: m1 Launch");
        using var index = new LinkIndex(_events, [new LinkIndexTestIndexer()]);

        // Act.
        var refusal = await index.RenewAsync([Held("release.lnk.txt")], TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(refusal);
        Assert.StartsWith("id: r1", await File.ReadAllTextAsync(_folder.Combine("release.lnk.txt"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    private void Write(string relative, params string[] lines)
    {
        var path = _folder.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join('\n', lines));
    }

    private string Held(params string[] relative) => Path.GetFullPath(_folder.Combine(relative)).Replace('\\', '/');
}
