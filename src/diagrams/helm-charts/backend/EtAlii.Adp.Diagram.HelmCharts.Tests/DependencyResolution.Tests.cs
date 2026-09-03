using Xunit;

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// Every state transition of the three-state matcher (Requirement 5.2), including the alias
/// and stripped-archive cases.
/// </summary>
public class DependencyResolutionTests
{
    [Fact]
    public void AVendoredDirectory_ResolvesItsDeclaration()
    {
        // Arrange.
        var dependency = Declared("redis");
        var entry = Vendored("redis", "charts/redis");

        // Act.
        var result = DependencyResolution.Match([dependency], [entry]);

        // Assert.
        var resolved = Assert.Single(result.Dependencies);
        Assert.True(resolved.IsResolved);
        Assert.Same(entry, resolved.Vendored);
        Assert.Empty(result.Undeclared);
    }

    [Fact]
    public void AnAliasedDependency_StillMatchesTheNameHelmVendorsUnder()
    {
        // Arrange.
        // helm dependency build writes charts/redis even when the dependency is mounted as
        // "cache": the alias renames the mount, not the folder.
        var dependency = Declared("redis", alias: "cache");

        // Act.
        var result = DependencyResolution.Match([dependency], [Vendored("redis", "charts/redis")]);

        // Assert.
        Assert.True(Assert.Single(result.Dependencies).IsResolved);
    }

    [Fact]
    public void AnEntryNamedByTheAlias_AlsoMatches()
    {
        // Arrange.
        // Hand-vendored trees sometimes carry the alias as the folder name; respecting the
        // alias means that shape resolves too.
        var dependency = Declared("redis", alias: "cache");

        // Act.
        var result = DependencyResolution.Match([dependency], [Vendored("cache", "charts/cache")]);

        // Assert.
        Assert.True(Assert.Single(result.Dependencies).IsResolved);
    }

    [Fact]
    public void AStrippedArchiveName_Matches()
    {
        // Arrange.
        // The reader already stripped common-2.31.4.tgz to "common"; the matcher sees names.
        var result = DependencyResolution.Match(
            [Declared("common")],
            [Vendored("common", "charts/common-2.31.4.tgz", sealedEntry: true)]);

        // Assert.
        Assert.True(Assert.Single(result.Dependencies).IsResolved);
    }

    [Fact]
    public void NothingVendored_IsAnOpenEndAndNoMore()
    {
        // Arrange & Act.
        var result = DependencyResolution.Match([Declared("alertmanager")], []);

        // Assert.
        var unvendored = Assert.Single(result.Dependencies);
        Assert.False(unvendored.IsResolved);
        Assert.Null(unvendored.Vendored);
        Assert.Empty(result.Undeclared);
    }

    [Fact]
    public void AVendoredEntryNoOneDeclares_IsUndeclared()
    {
        // Arrange & Act.
        var result = DependencyResolution.Match(
            [Declared("redis")],
            [Vendored("redis", "charts/redis"), Vendored("stray", "charts/stray")]);

        // Assert.
        Assert.Equal("stray", Assert.Single(result.Undeclared).EntryName);
    }

    [Fact]
    public void TwoDeclarationsOfOneChart_BothResolveToTheSameEntry()
    {
        // Arrange.
        // Helm allows the same chart mounted twice under different aliases; both point at
        // the one vendored folder.
        var first = Declared("redis", alias: "cache");
        var second = Declared("redis", alias: "queue");
        var entry = Vendored("redis", "charts/redis");

        // Act.
        var result = DependencyResolution.Match([first, second], [entry]);

        // Assert.
        Assert.Equal(2, result.Dependencies.Count);
        Assert.All(result.Dependencies, resolved => Assert.Same(entry, resolved.Vendored));
        Assert.Empty(result.Undeclared);
    }

    [Fact]
    public void MatchingIsOrdinal_SoCaseMatters()
    {
        // Arrange & Act.
        var result = DependencyResolution.Match([Declared("Redis")], [Vendored("redis", "charts/redis")]);

        // Assert.
        Assert.False(Assert.Single(result.Dependencies).IsResolved);
        Assert.Single(result.Undeclared);
    }

    private static DependencyDeclaration Declared(string name, string? alias = null) =>
        new(name, alias, "1.0.0", "https://charts.example.com", null, ConditionState.None, 1);

    private static VendoredEntry Vendored(string entryName, string relativePath, bool sealedEntry = false) =>
        new(entryName, relativePath, sealedEntry, sealedEntry ? null : entryName, "1.0.0", "application", 0, 0, null);
}
