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
        var matching = new StubProvider(ContextScope.Hierarchy, "matching");
        var other = new StubProvider(ContextScope.Unspecified, "other");
        var resolver = new ContextActionResolver(new IContextActionProvider[] { matching, other });

        var groups = await resolver.DiscoverAsync(Target(), CancellationToken.None);

        Assert.Equal(new[] { "matching" }, groups.SelectMany(g => g.Actions).Select(a => a.Id));
        Assert.False(other.WasConsulted);
    }

    [Fact]
    public async Task DiscoverAsync_WithSeveralMatchingProviders_ConcatenatesTheirGroupsInRegistrationOrder()
    {
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new StubProvider(ContextScope.Hierarchy, "first"),
            new StubProvider(ContextScope.Hierarchy, "second"),
        });

        var groups = await resolver.DiscoverAsync(Target(), CancellationToken.None);

        Assert.Equal(2, groups.Count);
        Assert.Equal("first", groups[0].Actions.Single().Id);
        Assert.Equal("second", groups[1].Actions.Single().Id);
    }

    [Fact]
    public async Task DiscoverAsync_WithNoProvidersAtAll_YieldsNoGroups()
    {
        var resolver = new ContextActionResolver(Array.Empty<IContextActionProvider>());

        var groups = await resolver.DiscoverAsync(Target(), CancellationToken.None);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task ResolveByActionIdAsync_FindsTheProviderOfferingThatAction()
    {
        var second = new StubProvider(ContextScope.Hierarchy, "second");
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new StubProvider(ContextScope.Hierarchy, "first"),
            second,
        });

        var owner = await resolver.ResolveByActionIdAsync(Target(), "second", CancellationToken.None);

        Assert.NotNull(owner);
        Assert.Same(second, owner.Provider);
        Assert.Equal("second", owner.Action.Id);
    }

    [Fact]
    public async Task ResolveByActionIdAsync_ForAnActionNobodyOffers_ResolvesToNothing()
    {
        var resolver = new ContextActionResolver(new IContextActionProvider[] { new StubProvider(ContextScope.Hierarchy, "known") });

        var owner = await resolver.ResolveByActionIdAsync(Target(), "unknown", CancellationToken.None);

        Assert.Null(owner);
    }

    [Fact]
    public async Task ResolveByShortcutAsync_FindsTheActionBoundToThatExactKeyAndModifiers()
    {
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new StubProvider(ContextScope.Hierarchy, "plain", shortcut: new ContextShortcutDefinition("F2")),
            new StubProvider(ContextScope.Hierarchy, "shifted", shortcut: new ContextShortcutDefinition("F2", Shift: true)),
        });

        var owner = await resolver.ResolveByShortcutAsync(Target(), new ContextShortcutDefinition("F2", Shift: true), CancellationToken.None);

        Assert.NotNull(owner);
        Assert.Equal("shifted", owner.Action.Id);
    }

    [Fact]
    public async Task ResolveByShortcutAsync_ForAShortcutOnAnUnavailableAction_ResolvesToNothing()
    {
        var resolver = new ContextActionResolver(new IContextActionProvider[]
        {
            new StubProvider(ContextScope.Hierarchy, "blocked", shortcut: new ContextShortcutDefinition("F2"), available: false),
        });

        var owner = await resolver.ResolveByShortcutAsync(Target(), new ContextShortcutDefinition("F2"), CancellationToken.None);

        Assert.Null(owner);
    }

    private sealed class StubProvider : IContextActionProvider
    {
        private readonly string _actionId;
        private readonly ContextShortcutDefinition? _shortcut;
        private readonly bool _available;

        public StubProvider(ContextScope scope, string actionId, ContextShortcutDefinition? shortcut = null, bool available = true)
        {
            Scope = scope;
            _actionId = actionId;
            _shortcut = shortcut;
            _available = available;
        }

        public ContextScope Scope { get; }

        public bool WasConsulted { get; private set; }

        public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
        {
            WasConsulted = true;
            var group = new ContextActionGroupDefinition(new[]
            {
                new ContextActionDefinition(_actionId, _actionId, "mdi-circle", _shortcut, _available),
            });
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(new[] { group });
        }

        public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionResult.Completed());

        public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ContextValidationResult.Accepted);

        public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ContextCommitResult.Succeeded);
    }
}
