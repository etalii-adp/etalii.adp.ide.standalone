using EtAlii.Adp.Backend.Context;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The resolver's whole point is that it knows no action of its own, so every test here
/// uses stub providers - naming a real one would prove nothing about provider-agnosticism.
/// </summary>
public class ContextActionResolverTests
{
    private static ContextTarget Target(ContextScope scope = ContextScope.Hierarchy) =>
        new(scope, @"C:\root\item.txt", IsContainer: false, ShortGuid.NewShortGuid());

    [Fact]
    public async Task DiscoverAsync_ConsultsOnlyProvidersWhoseScopeMatches()
    {
        var matching = new ContextActionResolverStubProvider(ContextScope.Hierarchy, "matching");
        var other = new ContextActionResolverStubProvider(ContextScope.Unspecified, "other");
        var resolver = new ContextActionResolver(new IContextActionProvider[] { matching, other });

        var groups = await resolver.DiscoverAsync(Target(), TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "matching" }, groups.SelectMany(g => g.Actions).Select(a => a.Id));
        Assert.False(other.WasConsulted);
    }

    [Fact]
    public async Task DiscoverAsync_WithSeveralMatchingProviders_ConcatenatesTheirGroupsInRegistrationOrder()
    {
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "first"),
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "second"),
        });

        var groups = await resolver.DiscoverAsync(Target(), TestContext.Current.CancellationToken);

        Assert.Equal(2, groups.Count);
        Assert.Equal("first", groups[0].Actions.Single().Id);
        Assert.Equal("second", groups[1].Actions.Single().Id);
    }

    [Fact]
    public async Task DiscoverAsync_WithNoProvidersAtAll_YieldsNoGroups()
    {
        var resolver = new ContextActionResolver(Array.Empty<IContextActionProvider>());

        var groups = await resolver.DiscoverAsync(Target(), TestContext.Current.CancellationToken);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task ResolveByActionIdAsync_FindsTheProviderOfferingThatAction()
    {
        var second = new ContextActionResolverStubProvider(ContextScope.Hierarchy, "second");
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "first"),
            second,
        });

        var owner = await resolver.ResolveByActionIdAsync(Target(), "second", TestContext.Current.CancellationToken);

        Assert.NotNull(owner);
        Assert.Same(second, owner.Provider);
        Assert.Equal("second", owner.Action.Id);
    }

    [Fact]
    public async Task ResolveByActionIdAsync_ForAnActionNobodyOffers_ResolvesToNothing()
    {
        var resolver = new ContextActionResolver(new IContextActionProvider[] { new ContextActionResolverStubProvider(ContextScope.Hierarchy, "known") });

        var owner = await resolver.ResolveByActionIdAsync(Target(), "unknown", TestContext.Current.CancellationToken);

        Assert.Null(owner);
    }

    [Fact]
    public async Task ResolveByShortcutAsync_FindsTheActionBoundToThatExactKeyAndModifiers()
    {
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "plain", shortcut: new ContextShortcutDefinition("F2")),
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "shifted", shortcut: new ContextShortcutDefinition("F2", Shift: true)),
        });

        var owner = await resolver.ResolveByShortcutAsync(Target(), new ContextShortcutDefinition("F2", Shift: true), TestContext.Current.CancellationToken);

        Assert.NotNull(owner);
        Assert.Equal("shifted", owner.Action.Id);
    }

    [Fact]
    public async Task ResolveByShortcutAsync_ForAShortcutOnAnUnavailableAction_ResolvesToNothing()
    {
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "blocked", shortcut: new ContextShortcutDefinition("F2"), available: false),
        });

        var owner = await resolver.ResolveByShortcutAsync(Target(), new ContextShortcutDefinition("F2"), TestContext.Current.CancellationToken);

        Assert.Null(owner);
    }

}
