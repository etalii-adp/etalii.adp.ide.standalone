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
    private static ContextTarget TargetOf(ToolOrigin origin) =>
        new(ContextScope.DiagramElement, "C:/root/vocabulary.ttl", IsContainer: false, ShortGuid.NewShortGuid(),
            Root: ProjectRoot.From("C:/root"), WatchId: default, ElementId: "res:http://example.org/Thing", Origin: origin);

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
        Assert.Equal(new[] { "matching" }, groups.ActionIds());
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
        var shapes = new ToolOrigin("w3c", "shacl");
        var scheme = new ToolOrigin("w3c", "skos");
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
        var reading = new ToolOrigin("w3c", "shacl");
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
        var shapes = new ToolOrigin("w3c", "shacl");
        var scheme = new ToolOrigin("w3c", "skos");
        var resolver = new ContextActionResolver(
        [
            new ContextActionResolverStubProvider(ContextScope.DiagramElement, "family.rename"),
            new ContextActionResolverStubProvider(ContextScope.DiagramElement, "shacl.deactivate", answersFor: shapes),
            new ContextActionResolverStubProvider(ContextScope.DiagramElement, "skos.broader", answersFor: scheme),
        ]);

        // Act.
        var groups = await resolver.DiscoverAsync(TargetOf(shapes), TestContext.Current.CancellationToken);

        // Assert: the other reading's verbs are absent, not merely ordered lower.
        var ids = groups.ActionIds();
        Assert.Equal(["family.rename", "shacl.deactivate"], ids);
    }

    [Fact]
    public async Task ResolveDropAsync_AsksAProviderThatDoesNotResolveDrops_ThroughItsMenu()
    {
        // Arrange: every module that has not opted in answers a drop exactly as it answers its action id.
        var offering = new ContextActionResolverStubProvider(ContextScope.Hierarchy, "add-child");
        var resolver = new ContextActionResolver(new IContextActionProvider[] { offering });

        // Act.
        var dropped = await resolver.ResolveDropAsync(Target(), "add-child", TestContext.Current.CancellationToken);
        var missing = await resolver.ResolveDropAsync(Target(), "add-sibling", TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(dropped);
        Assert.Same(offering, dropped.Provider);
        Assert.Equal("add-child", dropped.Action.Id);
        Assert.Null(missing);
    }

    [Fact]
    public async Task ResolveDropAsync_AsksAProviderThatResolvesDrops_ByTheEntry_NotByItsMenu()
    {
        // Arrange: its menu offers nothing of the kind, as a C4 element's menu offers no add.
        var dropping = new DroppingProvider("menu-only", new ContextActionDefinition("add-inside", "Add inside", "mdi-plus"));
        var resolver = new ContextActionResolver(new IContextActionProvider[] { dropping });

        // Act.
        var dropped = await resolver.ResolveDropAsync(Target(), "add-inside", TestContext.Current.CancellationToken);
        var byMenu = await resolver.ResolveDropAsync(Target(), "menu-only", TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(dropped);
        Assert.Same(dropping, dropped.Provider);
        Assert.Equal("add-inside", dropped.Action.Id);
        Assert.Null(byMenu);
    }

    [Fact]
    public async Task ResolveDropAsync_HandsBackARefusedDrop_WithItsReason_AndAsksTheNextProviderWhenOneAnswersNothing()
    {
        // Arrange.
        var silent = new DroppingProvider("x", null);
        var refusing = new DroppingProvider("x", new ContextActionDefinition("add-inside", "Add inside", "mdi-plus", Available: false, UnavailableReason: "Not on that."));
        var resolver = new ContextActionResolver(new IContextActionProvider[] { silent, refusing });

        // Act.
        var dropped = await resolver.ResolveDropAsync(Target(), "add-inside", TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(dropped);
        Assert.Same(refusing, dropped.Provider);
        Assert.False(dropped.Action.Available);
        Assert.Equal("Not on that.", dropped.Action.UnavailableReason);
    }

    /// <summary>A provider that answers drops itself: one answer for every drop, and a menu of one other action.</summary>
    private sealed class DroppingProvider(string menuActionId, ContextActionDefinition? drop) : IContextActionProvider, IContextDropProvider
    {
        private readonly ContextActionResolverStubProvider _menu = new(ContextScope.Hierarchy, menuActionId);

        public ContextScope Scope => ContextScope.Hierarchy;

        public ValueTask<ContextActionDefinition?> ResolveDropAsync(ContextTarget target, string dropActionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(drop is not null && drop.Id == dropActionId ? drop : null);

        public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken) =>
            _menu.DiscoverAsync(target, cancellationToken);

        public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
            _menu.ExecuteAsync(target, actionId, cancellationToken);

        public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
            _menu.ValidateAsync(target, actionId, value, cancellationToken);

        public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken) =>
            _menu.CommitAsync(target, actionId, value, text, cancellationToken);
    }
}
