using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using Xunit;

namespace EtAlii.Adp.Context.Tests;

/// <summary>
/// The resolver's whole point is that it knows no action of its own, so every test here
/// uses stub providers - naming a real one would prove nothing about provider-agnosticism.
/// </summary>
public class ContextActionResolverTests
{
    private static ContextTarget Target(ContextScope scope = ContextScope.Hierarchy) =>
        new(scope, @"C:\root\item.txt", IsContainer: false, ShortGuid.NewShortGuid());

    /// <summary>A diagram element target resolved through one reading of a multiply-registered file.</summary>
    private static ContextTarget TargetOf(DiagramOrigin origin) =>
        new(ContextScope.DiagramElement, @"C:/root/vocabulary.ttl", IsContainer: false, ShortGuid.NewShortGuid(),
            RootPath: @"C:/root", WatchId: default, ElementId: "res:http://example.org/Thing", Origin: origin);

    [Fact]
    public async Task DiscoverAsync_ConsultsOnlyProvidersWhoseScopeMatches()
    {
        // Arrange.
        var matching = new ContextActionResolverStubProvider(ContextScope.Hierarchy, "matching");
        var other = new ContextActionResolverStubProvider(ContextScope.Unspecified, "other");
        var resolver = new ContextActionResolver(new IContextActionProvider[] { matching, other });

        // Act.
        var groups = await resolver.DiscoverAsync(Target(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(new[] { "matching" }, groups.SelectMany(g => g.Actions).Select(a => a.Id));
        Assert.False(other.WasConsulted);
    }

    [Fact]
    public async Task DiscoverAsync_WithSeveralMatchingProviders_ConcatenatesTheirGroupsInRegistrationOrder()
    {
        // Arrange.
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "first"),
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "second"),
        });

        // Act.
        var groups = await resolver.DiscoverAsync(Target(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(2, groups.Count);
        Assert.Equal("first", groups[0].Actions.Single().Id);
        Assert.Equal("second", groups[1].Actions.Single().Id);
    }

    [Fact]
    public async Task DiscoverAsync_WithNoProvidersAtAll_YieldsNoGroups()
    {
        // Arrange.
        var resolver = new ContextActionResolver(Array.Empty<IContextActionProvider>());

        // Act.
        var groups = await resolver.DiscoverAsync(Target(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(groups);
    }

    [Fact]
    public async Task ResolveByActionIdAsync_FindsTheProviderOfferingThatAction()
    {
        // Arrange.
        var second = new ContextActionResolverStubProvider(ContextScope.Hierarchy, "second");
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "first"),
            second,
        });

        // Act.
        var owner = await resolver.ResolveByActionIdAsync(Target(), "second", TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(owner);
        Assert.Same(second, owner.Provider);
        Assert.Equal("second", owner.Action.Id);
    }

    [Fact]
    public async Task ResolveByActionIdAsync_ForAnActionNobodyOffers_ResolvesToNothing()
    {
        // Arrange.
        var resolver = new ContextActionResolver(new IContextActionProvider[] { new ContextActionResolverStubProvider(ContextScope.Hierarchy, "known") });

        // Act.
        var owner = await resolver.ResolveByActionIdAsync(Target(), "unknown", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Null(owner);
    }

    [Fact]
    public async Task ResolveByShortcutAsync_FindsTheActionBoundToThatExactKeyAndModifiers()
    {
        // Arrange.
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "plain", shortcut: new ContextShortcutDefinition("F2")),
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "shifted", shortcut: new ContextShortcutDefinition("F2", Shift: true)),
        });

        // Act.
        var owner = await resolver.ResolveByShortcutAsync(Target(), new ContextShortcutDefinition("F2", Shift: true), TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(owner);
        Assert.Equal("shifted", owner.Action.Id);
    }

    [Fact]
    public async Task ResolveByShortcutAsync_ForAShortcutOnAnUnavailableAction_ResolvesToNothing()
    {
        // Arrange.
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new ContextActionResolverStubProvider(ContextScope.Hierarchy, "blocked", shortcut: new ContextShortcutDefinition("F2"), available: false),
        });

        // Act.
        var owner = await resolver.ResolveByShortcutAsync(Target(), new ContextShortcutDefinition("F2"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Null(owner);
    }

    [Fact]
    public async Task ResolveByShortcutAsync_ForOneReadingsTarget_IsNotAnsweredByASiblingReading()
    {
        // Arrange.
        // NO CONSUMER YET, deliberately. Resolving a shortcut takes the first match PER PROVIDER,
        // flattening that provider s own groups - so a family whose readings delegate inside one
        // provider has flatten order as its shortcut order, and ordering the groups is the whole fix
        // there. This covers the case where a reading registers a provider of ITS OWN beside the
        // family s: then two providers each answer, container registration order decides, and no
        // ordering within either reaches it. No reading does that today; one reasonably might.
        var shapes = new DiagramOrigin("w3c", "shacl");
        var scheme = new DiagramOrigin("w3c", "skos");
        var skos = new ContextActionResolverStubProvider(
            ContextScope.DiagramElement, "skos.remove-pair", new ContextShortcutDefinition("Delete"), answersFor: scheme);
        var shacl = new ContextActionResolverStubProvider(
            ContextScope.DiagramElement, "shacl.deactivate", new ContextShortcutDefinition("Delete"), answersFor: shapes);
        var resolver = new ContextActionResolver([skos, shacl]);

        // Act: skos is registered first, which is the losing order before the origin decided it.
        var owner = await resolver.ResolveByShortcutAsync(
            TargetOf(shapes), new ContextShortcutDefinition("Delete"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(owner);
        Assert.Equal("shacl.deactivate", owner.Action.Id);
    }

    [Fact]
    public async Task ResolveByShortcutAsync_ForATargetWithNoOrigin_KeepsTheBehaviourItHadBeforeTheFieldExisted()
    {
        // Arrange: a resolver that has not adopted the origin yet supplies none, and a null origin
        // means 'not known' rather than 'no reading' - so the reading's provider stays out and the
        // family answers, exactly as it did before. This is what makes the migration incremental.
        var reading = new DiagramOrigin("w3c", "shacl");
        var family = new ContextActionResolverStubProvider(
            ContextScope.DiagramElement, "family.remove-statement", new ContextShortcutDefinition("Delete"));
        var shapes = new ContextActionResolverStubProvider(
            ContextScope.DiagramElement, "shacl.deactivate", new ContextShortcutDefinition("Delete"), answersFor: reading);
        var resolver = new ContextActionResolver([family, shapes]);

        // Act.
        var owner = await resolver.ResolveByShortcutAsync(
            Target(ContextScope.DiagramElement), new ContextShortcutDefinition("Delete"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(owner);
        Assert.Equal("family.remove-statement", owner.Action.Id);
    }

    [Fact]
    public async Task DiscoverAsync_ForOneOriginsTarget_ConcatenatesTheFamilyAndThatReadingOnly()
    {
        // Arrange: two readings over one file, plus the family beneath both.
        var shapes = new DiagramOrigin("w3c", "shacl");
        var scheme = new DiagramOrigin("w3c", "skos");
        var resolver = new ContextActionResolver(
        [
            new ContextActionResolverStubProvider(ContextScope.DiagramElement, "family.rename"),
            new ContextActionResolverStubProvider(ContextScope.DiagramElement, "shacl.deactivate", answersFor: shapes),
            new ContextActionResolverStubProvider(ContextScope.DiagramElement, "skos.broader", answersFor: scheme),
        ]);

        // Act.
        var groups = await resolver.DiscoverAsync(TargetOf(shapes), TestContext.Current.CancellationToken);

        // Assert: the other reading's verbs are absent, not merely ordered lower.
        var ids = groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();
        Assert.Equal(["family.rename", "shacl.deactivate"], ids);
    }
}
