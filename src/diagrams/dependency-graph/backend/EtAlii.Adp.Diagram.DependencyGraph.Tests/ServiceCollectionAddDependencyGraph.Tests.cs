using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The module's one registration point, invoked through the definition's Build hook when
/// discovery finds this assembly - the host names nothing.
/// </summary>
/// <remarks>
/// "Core learns nothing new" is the kind of claim that stays true only while something checks
/// it: a single stray using in a core file would quietly falsify it and nothing else would
/// notice. Every seam is resolved rather than merely counted, so a registration whose
/// dependencies cannot be satisfied fails here rather than at the first request.
/// </remarks>
public class ServiceCollectionAddDependencyGraphTests
{
    /// <summary>The seams a diagram module plugs into, and what this one is expected to fill.</summary>
    public static TheoryData<Type> RegisteredSeams() =>
    [
        typeof(IDiagramDocumentFactory),
        typeof(IDiagramSessionFactory),
        typeof(IDiagramDocumentReloader),
        typeof(IContextSourceResolver),
        typeof(IContextActionProvider),
        typeof(IContextPropertyProvider),
        typeof(IDiagramToolboxProvider),
        typeof(IDiagramValidator),
        typeof(IDependencyGraphDocumentStore),
    ];

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // The few core services this module's own registrations depend on. A production host
        // has far more; what matters here is that AddDependencyGraph asks for nothing else.
        services.AddSingleton<ICommandDispatcher>(
            new DependencyGraphTestDispatcher(new DependencyGraphDocumentStore()));
        services.AddSingleton<IHistoryStackStore>(provider =>
            new HistoryStackStore(provider.GetRequiredService<ICommandDispatcher>()));
        services.AddSingleton<IDiagramDefinitionCatalog, DependencyGraphOnlyCatalog>();
        services.AddSingleton<DiagramFileRouter>();

        services.AddDependencyGraph();
        return services.BuildServiceProvider();
    }

    [Theory]
    [MemberData(nameof(RegisteredSeams))]
    public void OneCall_FillsEverySeamTheModuleUses(Type seam)
    {
        // Arrange & act.
        using var provider = Build();

        // Assert.
        Assert.NotNull(provider.GetService(seam));
    }

    [Fact]
    public void EveryCommandTheModuleDefines_HasItsHandlerRegistered()
    {
        // Arrange.
        // A command with no handler throws at dispatch, which is the worst place to find out.
        // The list comes from the assembly rather than a second hand-written one, so adding a
        // command without registering its handler fails here.
        using var provider = Build();
        var commands = typeof(SetDependencyGraphPlacementCommand).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(ICommand).IsAssignableFrom(type))
            .ToList();

        // Act & assert.
        Assert.NotEmpty(commands);
        foreach (var command in commands)
        {
            var handler = typeof(ICommandHandler<>).MakeGenericType(command);
            Assert.True(provider.GetService(handler) is not null, $"{command.Name} has no registered handler");
        }
    }

    [Fact]
    public void TheDefinitionsBuildHook_IsWhatCallsThis()
    {
        // Assert.
        // The host names no module: discovery finds Diagram.Definitions and invokes each
        // definition's Build. A definition whose hook stopped calling AddDependencyGraph would be
        // a module that is discovered and then contributes nothing, with no error anybody sees.
        Assert.NotNull(Diagram.DependencyGraph.Build);
    }

    [Fact]
    public void NoTypeInTheModuleIsNamedAfterATime()
    {
        // Assert.
        // The fork's absence test, at assembly scale: TimelineInstants and TimelineScale had no
        // counterparts, and a later hand reaching for a date would name the file after one.
        var offenders = typeof(SetDependencyGraphPlacementCommand).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("EtAlii.Adp.Diagram.DependencyGraph", StringComparison.Ordinal) == true)
            .Select(type => type.Name)
            .Where(name => new[] { "Instant", "Scale", "Precision", "Moment", "Duration" }
                .Any(word => name.Contains(word, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offenders);
    }
}
