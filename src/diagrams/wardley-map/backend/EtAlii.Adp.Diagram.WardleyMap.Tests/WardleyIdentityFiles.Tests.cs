using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// Where a map's ids are kept (spec 071, T012, Q3 and FBL 8.7): a map opened and not edited writes no file; a new map keeps
/// its ids in the <c>identities:</c> block of a registration its first save writes, and a host never creates a legacy
/// identities file; an existing legacy file is kept in the shape it has, its entries changed by splices.
/// </summary>
public sealed class WardleyIdentityFilesTests : IDisposable
{
    private const string Map = "component Kettle [0.5, 0.4]\ncomponent Power [0.1, 0.7]\n";

    private readonly TestFolder _testFolder = TestFolder.Create("wardley-identity-files");
    private readonly ServiceProvider _services;
    private readonly string _path;

    public WardleyIdentityFilesTests()
    {
        _path = IoPath.Combine(_testFolder.Path, "map.owm");
        _services = Provider();
        File.WriteAllText(_path, Map);
    }

    public void Dispose()
    {
        _services.Dispose();
        _testFolder.Dispose();
    }

    private static ServiceProvider Provider() => new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddWardleyMap().BuildServiceProvider();

    private string Registration => WardleyIdentityFiles.RegistrationPathFor(_path);

    private string Legacy => WardleyIdentityFiles.LegacyPathFor(_path);

    private string[] Files() => [.. Directory.GetFiles(_testFolder.Path).Select(IoPath.GetFileName).Order(StringComparer.Ordinal)!];

    private static async Task<IReadOnlyList<WardleyIdentityEntry>> IdentitiesAsync(ServiceProvider services, string path)
    {
        var store = services.GetRequiredService<IWardleyDocumentStore>();
        await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        return store.Identities(path);
    }

    private async Task<CommandResult> ExecuteAsync(ICommand command)
    {
        await IdentitiesAsync(_services, _path);
        var history = _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_testFolder.Path));
        return await history.ExecuteAsync(command, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AMapOpenedAndNotEdited_WritesNoFile()
    {
        // Act.
        var identities = await IdentitiesAsync(_services, _path);

        // Assert.
        Assert.NotEmpty(identities);
        Assert.Equal(["map.owm"], Files());
    }

    [Fact]
    public async Task ANewMapsFirstSave_WritesARegistrationHoldingItsIds_AndNoLegacyFile()
    {
        // Arrange.
        var ids = await IdentitiesAsync(_services, _path);
        var kettle = ids.Single(entry => entry.Key == "Kettle").Id;

        // Act.
        var moved = await ExecuteAsync(new MoveWardleyElementCommand(_path, kettle, 0.6d, 0.4d));
        using var reopened = Provider();
        var again = await IdentitiesAsync(reopened, _path);

        // Assert.
        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(["map.adp", "map.owm"], Files());
        Assert.False(File.Exists(Legacy));
        var registration = await File.ReadAllTextAsync(Registration, TestContext.Current.CancellationToken);
        Assert.StartsWith(Diagram.WardleyMap.Origin.ToString(), registration, StringComparison.Ordinal);
        Assert.Contains(kettle, registration, StringComparison.Ordinal);
        Assert.Equal(ids, again);
    }

    [Fact]
    public async Task ALegacyFile_IsKeptInItsShape_AndARenameChangesItsKeyAlone()
    {
        // Arrange: written as the tool writes it, but on one line, which the splices keep.
        const string legacy = "[{\"id\": \"kettle-id\", \"kind\": \"component\", \"key\": \"Kettle\"}, {\"id\": \"power-id\", \"kind\": \"component\", \"key\": \"Power\"}]\n";
        await File.WriteAllTextAsync(Legacy, legacy, TestContext.Current.CancellationToken);

        // Act.
        var renamed = await ExecuteAsync(new RenameWardleyElementCommand(_path, "kettle-id", "Electric Kettle"));
        using var reopened = Provider();
        var again = await IdentitiesAsync(reopened, _path);

        // Assert.
        Assert.True(renamed.IsSuccess, renamed.Error);
        Assert.Equal(["map.identities.json", "map.owm"], Files());
        Assert.Equal(legacy.Replace("\"Kettle\"", "\"Electric Kettle\"", StringComparison.Ordinal), await File.ReadAllTextAsync(Legacy, TestContext.Current.CancellationToken));
        Assert.Equal(["kettle-id", "power-id"], again.Where(entry => entry.Kind == WardleyIdentityKind.Component).Select(entry => entry.Id));
    }
}
