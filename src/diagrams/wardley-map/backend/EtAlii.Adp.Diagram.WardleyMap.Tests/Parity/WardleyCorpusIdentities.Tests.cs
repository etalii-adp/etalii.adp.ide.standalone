using System.Text;
using System.Text.Json;
using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

/// <summary>
/// Every id stored today is the id of the same element once the binding keeps them (spec 071, T012; the second half of
/// Requirement 6.4): each checked-in identities file of the corpus, read through the binding, gives every stored entry
/// back byte for byte, its key as the rule's <c>sidecar.key</c> computes it, an attitude's two numbers included.
/// </summary>
public sealed class WardleyCorpusIdentitiesTests
{
    /// <summary>The documents of the corpus that have an identities file beside them.</summary>
    public static TheoryData<string> Kept() => [.. KeptNames()];

    [Theory]
    [MemberData(nameof(Kept))]
    public async Task EveryStoredEntry_IsReadBackThroughTheBinding_ByteForByte(string name)
    {
        // Arrange.
        var stored = await StoredAsync(name);
        var text = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(IoPath.Combine(new WardleyTranscript().SourceFolder, name), TestContext.Current.CancellationToken));

        // Act.
        var body = WardleyBody.Edit(LineDocument.Parse(text), stored);
        var read = body.Identities();

        // Assert.
        Assert.NotEmpty(stored);
        Assert.All(stored, entry => Assert.Contains(entry, read));
        Assert.All(stored, entry => Assert.Contains(body.Reading.Elements, element => element.Id == entry.Id));
        Assert.Equal(read.Count, read.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TheCorpusKeepsAttitudes_SoTheirKeysAreAmongThoseRead()
    {
        // Act.
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in KeptNames())
        {
            kinds.UnionWith((await StoredAsync(name)).Select(entry => entry.Kind));
        }

        // Assert.
        Assert.Contains(WardleyIdentityKind.Attitude, kinds);
    }

    private static IEnumerable<string> KeptNames() =>
        new WardleyTranscript().Corpus().Select(document => document.Name).Where(name => File.Exists(SidecarOf(name)));

    private static string SidecarOf(string name) => WardleyIdentities.PathFor(IoPath.Combine(new WardleyTranscript().SourceFolder, name));

    private static async Task<IReadOnlyList<WardleyIdentityEntry>> StoredAsync(string name)
    {
        await using var stream = File.OpenRead(SidecarOf(name));
        var entries = await JsonSerializer.DeserializeAsync<List<WardleyIdentityEntry>>(stream, Options, TestContext.Current.CancellationToken);
        return entries!;
    }

    // The file's field names are the entry's in lower case, as the tool writes them.
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}
