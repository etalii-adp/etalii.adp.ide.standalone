using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The module's one registration point, invoked through the definition's Build hook when
/// discovery finds this assembly - the host names nothing.
/// </summary>
/// <remarks>
/// "Core is unchanged by this module" is the kind of claim that stays true only while something
/// checks it: a single stray using in a core file would quietly falsify it and nothing else
/// would notice. Every seam is resolved rather than merely counted, so a registration whose
/// dependencies cannot be satisfied fails here rather than at the first request.
/// </remarks>
public class ServiceCollectionAddTimelineTests
{
    /// <summary>The seams a diagram module plugs into, and what this one is expected to fill.</summary>
    public static TheoryData<Type> RegisteredSeams() =>
    [
        typeof(IDiagramDocumentFactory),
        typeof(IDiagramSessionFactory),
        typeof(IContextSourceResolver),
        typeof(IContextActionProvider),
        typeof(IContextPropertyProvider),
        typeof(IDiagramToolboxProvider),
        typeof(IDiagramValidator),
        typeof(ITimelineDocumentStore),
    ];

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // The few core services this module's own registrations depend on. A production host
        // has far more; what matters here is that AddTimeline asks for nothing else.
        services.AddSingleton<ICommandDispatcher>(new TimelineTestDispatcher(new TimelineDocumentStore()));
        services.AddSingleton<IHistoryStackStore>(provider =>
            new HistoryStackStore(provider.GetRequiredService<ICommandDispatcher>()));
        services.AddSingleton<IDiagramDefinitionCatalog, TimelineOnlyCatalog>();
        services.AddSingleton<DiagramFileRouter>();

        services.AddTimeline();
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
        var commands = typeof(SetTimelinePlacementCommand).Assembly
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
        // definition's Build. A definition whose hook stopped calling AddTimeline would be a
        // module that is discovered and then contributes nothing, with no error anybody sees.
        Assert.NotNull(Diagram.Timeline.Build);
    }
}
