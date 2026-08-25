using EtAlii.Adp.Backend.Context;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The routing between the property grid and whichever module owns what is selected.
/// </summary>
/// <remarks>
/// The resolver knows no property id of its own, exactly as the action resolver knows no action
/// id: a module contributes properties by registering a provider, and nothing in core learns
/// what they are.
/// </remarks>
public class ContextPropertyResolverTests
{
    private static ContextTarget Target(ContextScope scope = ContextScope.DiagramElement) => new(
        scope,
        ResolvedFullPath: "C:/project/model.dsl",
        IsContainer: false,
        SourceId: ShortGuid.NewShortGuid(),
        RootPath: "C:/project",
        ElementId: "element");

    [Fact]
    public async Task DescribeAsync_GathersFromEveryProviderForTheScope_InRegistrationOrder()
    {
        // Arrange.
        var first = new StubPropertyProvider(ContextScope.DiagramElement, [new ContextPropertyDefinition("a.one", "One", "1")]);
        var second = new StubPropertyProvider(ContextScope.DiagramElement, [new ContextPropertyDefinition("b.two", "Two", "2")]);
        var resolver = new ContextPropertyResolver([first, second]);

        // Act.
        var properties = await resolver.DescribeAsync(Target(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(["a.one", "b.two"], properties.Select(property => property.Id));
    }

    [Fact]
    public async Task DescribeAsync_IgnoresProvidersForAnotherScope()
    {
        // Arrange.
        var elsewhere = new StubPropertyProvider(ContextScope.Hierarchy, [new ContextPropertyDefinition("h.one", "One", "1")]);
        var resolver = new ContextPropertyResolver([elsewhere]);

        // Act.
        var properties = await resolver.DescribeAsync(Target(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(properties);
    }

    [Fact]
    public async Task SetAsync_ReachesTheProviderThatDescribesTheProperty()
    {
        // Arrange.
        var owner = new StubPropertyProvider(ContextScope.DiagramElement, [new ContextPropertyDefinition("b.two", "Two", "2")]);
        var other = new StubPropertyProvider(ContextScope.DiagramElement, [new ContextPropertyDefinition("a.one", "One", "1")]);
        var resolver = new ContextPropertyResolver([other, owner]);

        // Act.
        var result = await resolver.SetAsync(Target(), "b.two", "changed", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([("b.two", "changed")], owner.Writes);
        Assert.Empty(other.Writes);
    }

    [Fact]
    public async Task SetAsync_RefusesAPropertyItsOwnerMarkedReadOnly_AndSaysWhy()
    {
        // Arrange.
        // Enforced here rather than trusted to the client: a grid that has not caught up - or a
        // caller that is not the grid at all - must not be able to write a value the provider
        // said was not writable.
        const string reason = "Defined by the template this stage extends.";
        var provider = new StubPropertyProvider(
            ContextScope.DiagramElement,
            [new ContextPropertyDefinition("a.one", "One", "1", ContextPropertyEditor.Line, reason)]);
        var resolver = new ContextPropertyResolver([provider]);

        // Act.
        var result = await resolver.SetAsync(Target(), "a.one", "changed", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(reason, result.Error);
        Assert.Empty(provider.Writes);
    }

    [Fact]
    public async Task SetAsync_RefusesAPropertyNobodyOffers()
    {
        // Arrange.
        var provider = new StubPropertyProvider(ContextScope.DiagramElement, [new ContextPropertyDefinition("a.one", "One", "1")]);
        var resolver = new ContextPropertyResolver([provider]);

        // Act.
        var result = await resolver.SetAsync(Target(), "a.invented", "changed", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("a.invented", result.Error, StringComparison.Ordinal);
        Assert.Empty(provider.Writes);
    }

    [Fact]
    public async Task SetAsync_PassesOnWhatTheProviderRefused()
    {
        // Arrange.
        var provider = new StubPropertyProvider(
            ContextScope.DiagramElement,
            [new ContextPropertyDefinition("a.one", "One", "1")],
            refusal: "A name cannot be empty.");
        var resolver = new ContextPropertyResolver([provider]);

        // Act.
        var result = await resolver.SetAsync(Target(), "a.one", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("A name cannot be empty.", result.Error);
    }

    // ---- one broken module costs its own rows, and nothing more ------------------------

    [Fact]
    public async Task DescribeAsync_LosesOnlyTheRowsOfAProviderThatThrows()
    {
        // Arrange.
        var broken = new StubPropertyProvider(ContextScope.DiagramElement, [new ContextPropertyDefinition("a.one", "One", "1")]) { ThrowOnDescribe = true };
        var healthy = new StubPropertyProvider(ContextScope.DiagramElement, [new ContextPropertyDefinition("b.two", "Two", "2")]);
        var resolver = new ContextPropertyResolver([broken, healthy]);

        // Act.
        var properties = await resolver.DescribeAsync(Target(), TestContext.Current.CancellationToken);

        // Assert.
        // The panel shows what it can rather than going blank over somebody else's fault.
        Assert.Equal(["b.two"], properties.Select(property => property.Id));
    }

    [Fact]
    public async Task SetAsync_ReachesItsOwnerPastAProviderThatCannotDescribe()
    {
        // Arrange.
        var broken = new StubPropertyProvider(ContextScope.DiagramElement, []) { ThrowOnDescribe = true };
        var owner = new StubPropertyProvider(ContextScope.DiagramElement, [new ContextPropertyDefinition("b.two", "Two", "2")]);
        var resolver = new ContextPropertyResolver([broken, owner]);

        // Act.
        var result = await resolver.SetAsync(Target(), "b.two", "changed", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Equal(("b.two", "changed"), Assert.Single(owner.Writes));
    }

    [Fact]
    public async Task SetAsync_LetsTheOwnersOwnFailureTravel_RatherThanReportingSuccess()
    {
        // Arrange.
        var owner = new StubPropertyProvider(ContextScope.DiagramElement, [new ContextPropertyDefinition("a.one", "One", "1")]) { ThrowOnSet = true };
        var resolver = new ContextPropertyResolver([owner]);

        // Act and assert.
        // A write that failed must never come back accepted; swallowing this would tell the
        // grid its value landed while the document says otherwise.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await resolver.SetAsync(Target(), "a.one", "changed", TestContext.Current.CancellationToken));
    }
}
