using EtAlii.Adp.Files;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The identities held beside a document follow what the shared store does to it: kept while it
/// keeps the last good document, re-reconciled when it installs another (Requirement 4.3, R2.5).
/// </summary>
public sealed class WardleyDocumentStoreIdentityTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.WardleyDocumentStoreIdentityTests");

    private string Body => field ??= Copy();

    private string Copy()
    {
        var body = _folder.Combine("tea.owm");
        File.Copy(WardleyDocumentStoreTests.Minimal, body);
        return body;
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AMissingBody_KeepsItsIdentitiesOnReload_AndLosesThemOnlyToTheWatchersDelete()
    {
        // Arrange.
        var store = new WardleyDocumentStore(new FileWriter());
        await store.GetOrLoadAsync(Body, TestContext.Current.CancellationToken);
        var held = store.Identities(Body);
        Assert.NotEmpty(held);
        File.Delete(Body);

        // Act: a reload that finds no file keeps the last good document, and with it its ids.
        store.Reload(Body);
        var kept = store.Identities(Body);

        // Act: the watcher's evidence that it is gone.
        store.BodyDeleted(Body);

        // Assert.
        Assert.Equal(held, kept);
        Assert.Empty(store.Identities(Body));
    }

    [Fact]
    public async Task AnExternalEdit_KeepsTheIdsOfWhatSurvived_AndGivesTheNewcomerOne()
    {
        // Arrange.
        var store = new WardleyDocumentStore(new FileWriter());
        await store.GetOrLoadAsync(Body, TestContext.Current.CancellationToken);
        var before = store.Identities(Body);
        await File.AppendAllTextAsync(Body, "component Kettle [0.45, 0.57]\n", TestContext.Current.CancellationToken);

        // Act.
        store.Reload(Body);
        var after = store.Identities(Body);

        // Assert: a `git pull` does not reset a selection on what it left alone.
        Assert.All(before, entry => Assert.Contains(entry, after));
        Assert.Single(after.Except(before), entry => entry.Key == "Kettle");
    }

    [Fact]
    public async Task AForgottenDocument_ForgetsItsIdentities_AndReadsTheSidecarAfresh()
    {
        // Arrange: ids handed out on open and never written, as on a map opened and not edited.
        var store = new WardleyDocumentStore(new FileWriter());
        await store.GetOrLoadAsync(Body, TestContext.Current.CancellationToken);
        var minted = store.Identities(Body);

        // Act.
        store.Forget(Body);
        await store.GetOrLoadAsync(Body, TestContext.Current.CancellationToken);
        var again = store.Identities(Body);

        // Assert: no sidecar was written, so the next open mints its own; the old ones are not held.
        Assert.NotEqual(minted.Select(entry => entry.Id), again.Select(entry => entry.Id));
    }

    [Fact]
    public void TheIdentitiesOfAMapNotReadYet_AreNone_AndReadNothing()
    {
        // Arrange: identities are asked for a map that was read; this one was not.
        var store = new WardleyDocumentStore(new FileWriter());

        // Act.
        var identities = store.Identities(Body);

        // Assert: nothing minted, nothing read, and nothing kept to answer the read that follows.
        Assert.Empty(identities);
        Assert.Null(store.Get(Body));
    }

    [Fact]
    public async Task TheIdentitiesOfAMapReadLater_AreReconciledWithIt()
    {
        // Arrange: identities asked for before the map was read are not the ones kept for it.
        var store = new WardleyDocumentStore(new FileWriter());
        _ = store.Identities(Body);

        // Act.
        await store.GetOrLoadAsync(Body, TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotEmpty(store.Identities(Body));
    }

    [Fact]
    public async Task ASaveWhoseIdentitiesCannotBeKept_LandsTheMap_AndSaysSo()
    {
        // Arrange: a folder where the identity sidecar goes, which no platform lets a file replace.
        var store = new WardleyDocumentStore(new FileWriter());
        Directory.CreateDirectory(WardleyIdentities.PathFor(Body));
        var document = store.GetOrLoad(Body).Document;
        WardleyWriter.ReplaceLine(document, 1, "title Kept anyway");

        // Act.
        var saved = await store.SaveAsync(Body, document, TestContext.Current.CancellationToken);

        // Assert: the map landed, and the ids it hands out will not survive a restart.
        Assert.False(saved.Failed, saved.Error);
        Assert.Contains("title Kept anyway", await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("new ids", saved.Warning, StringComparison.Ordinal);
    }
}
