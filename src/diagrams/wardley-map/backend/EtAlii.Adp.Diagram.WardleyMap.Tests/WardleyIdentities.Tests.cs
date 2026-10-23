using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public sealed class WardleyIdentitiesTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("wardley-ids");
    private readonly string _folder;
    private readonly WardleyIdentities _identities = new(new FileWriter());

    public WardleyIdentitiesTests() => _folder = _testFolder.Path;

    public void Dispose()
    {
        _testFolder.Dispose();
    }

    private string BodyPath => IoPath.Combine(_folder, "map.owm");

    [Fact]
    public void PathFor_PutsTheSidecarBesideTheBody_WithTheSameBaseName()
    {
        // Act.
        var path = WardleyIdentities.PathFor(IoPath.Combine("some", "folder", "supply-chain.owm"));

        // Assert. Beside the `.owm`, never inside it (Requirement 3.7).
        Assert.Equal(IoPath.Combine("some", "folder", "supply-chain.identities.json"), path);
    }

    [Fact]
    public void PathFor_HandlesABodyWithNoFolder()
    {
        // Act.
        var path = WardleyIdentities.PathFor("map.owm");

        // Assert.
        Assert.Equal("map.identities.json", path);
    }

    [Fact]
    public void Read_ReturnsNothing_WhenThereIsNoSidecar()
    {
        // Act.
        var entries = _identities.Read(BodyPath);

        // Assert. Requirement 4.3 - a map authored elsewhere has no sidecar and is still usable.
        Assert.Empty(entries);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsTheEntries()
    {
        // Arrange.
        WardleyIdentityEntry[] written =
        [
            new("abc123", "component", "Cup of Tea"),
            new("def456", "link", "Cup of Tea->Kettle"),
            new("ghi789", "annotation", "1"),
        ];

        // Act.
        await _identities.WriteAsync(BodyPath, written, TestContext.Current.CancellationToken);
        var read = _identities.Read(BodyPath);

        // Assert.
        Assert.Equal(written, read);
    }

    [Fact]
    public async Task Write_ReplacesWhatWasThere_RatherThanAppending()
    {
        // Arrange.
        await _identities.WriteAsync(BodyPath, [new WardleyIdentityEntry("abc123", "component", "Old")], TestContext.Current.CancellationToken);

        // Act.
        await _identities.WriteAsync(BodyPath, [new WardleyIdentityEntry("def456", "component", "New")], TestContext.Current.CancellationToken);
        var read = _identities.Read(BodyPath);

        // Assert.
        Assert.Equal([new WardleyIdentityEntry("def456", "component", "New")], read);
    }

    [Fact]
    public async Task Write_LeavesNoTemporaryFileBehind()
    {
        // Act.
        await _identities.WriteAsync(BodyPath, [new WardleyIdentityEntry("abc123", "component", "Cup of Tea")], TestContext.Current.CancellationToken);

        // Assert. The sidecar is written atomically, and its scratch file must not litter the
        // project folder any more than the document's does.
        Assert.Equal(
            ["map.identities.json"],
            Directory.GetFiles(_folder).Select(IoPath.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Write_RemovesTheSidecar_WhenThereIsNothingLeftToKeep()
    {
        // Arrange.
        await _identities.WriteAsync(BodyPath, [new WardleyIdentityEntry("abc123", "component", "Cup of Tea")], TestContext.Current.CancellationToken);

        // Act.
        await _identities.WriteAsync(BodyPath, [], TestContext.Current.CancellationToken);

        // Assert. A stray empty file would only invite the question of what it is for.
        Assert.False(File.Exists(WardleyIdentities.PathFor(BodyPath)));
    }

    [Fact]
    public void Read_ReturnsNothing_WhenTheSidecarIsNotJson()
    {
        // Arrange. Requirement 4.5 - the sidecar is never the reason a map fails to open.
        File.WriteAllText(WardleyIdentities.PathFor(BodyPath), "this is not json {{{");

        // Act.
        var entries = _identities.Read(BodyPath);

        // Assert.
        Assert.Empty(entries);
    }

    [Fact]
    public void Read_ReturnsNothing_WhenTheSidecarIsJsonOfTheWrongShape()
    {
        // Arrange.
        File.WriteAllText(WardleyIdentities.PathFor(BodyPath), """{"not":"a list"}""");

        // Act.
        var entries = _identities.Read(BodyPath);

        // Assert.
        Assert.Empty(entries);
    }

    [Fact]
    public void Read_DiscardsAHalfWrittenEntry_AndKeepsTheRest()
    {
        // Arrange. An identity with no id, or no key to match it by, cannot do the one job it
        // exists for - but it must not cost the entries around it.
        File.WriteAllText(
            WardleyIdentities.PathFor(BodyPath),
            """
            [
              { "id": "abc123", "kind": "component", "key": "Cup of Tea" },
              { "id": "", "kind": "component", "key": "No id" },
              { "id": "def456", "kind": "component", "key": "" },
              { "id": "ghi789", "kind": "component", "key": "Kettle" }
            ]
            """);

        // Act.
        var entries = _identities.Read(BodyPath);

        // Assert.
        Assert.Equal(
            [
                new WardleyIdentityEntry("abc123", "component", "Cup of Tea"),
                new WardleyIdentityEntry("ghi789", "component", "Kettle"),
            ],
            entries);
    }

    [Fact]
    public async Task Remove_DeletesTheSidecar()
    {
        // Arrange.
        await _identities.WriteAsync(BodyPath, [new WardleyIdentityEntry("abc123", "component", "Cup of Tea")], TestContext.Current.CancellationToken);

        // Act.
        await _identities.RemoveAsync(BodyPath, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(File.Exists(WardleyIdentities.PathFor(BodyPath)));
    }

    [Fact]
    public async Task Remove_IsSilent_WhenThereIsNoSidecar()
    {
        // Act and assert. Nothing here throws for a file that is not there.
        await _identities.RemoveAsync(BodyPath, TestContext.Current.CancellationToken);
        Assert.False(File.Exists(WardleyIdentities.PathFor(BodyPath)));
    }

    [Fact]
    public async Task Write_DoesNotTouchTheOwmItself()
    {
        // Arrange. Requirement 3.7 - ADP's own data never goes into the document, not even as
        // a comment.
        await File.WriteAllTextAsync(BodyPath, "title Untouched\n", TestContext.Current.CancellationToken);

        // Act.
        await _identities.WriteAsync(BodyPath, [new WardleyIdentityEntry("abc123", "component", "Cup of Tea")], TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("title Untouched\n", await File.ReadAllTextAsync(BodyPath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_RefusesABodyPathThatIsNotOne(string? bodyPath)
    {
        // Act and assert.
        Assert.ThrowsAny<ArgumentException>(() => _identities.Read(bodyPath!));
    }
}
