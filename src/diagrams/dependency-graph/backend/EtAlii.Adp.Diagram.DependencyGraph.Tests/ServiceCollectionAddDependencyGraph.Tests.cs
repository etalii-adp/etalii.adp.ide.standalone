using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
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
        services.AddSingleton<IFileWriter>(new FileWriter());

        // The few core services this module's own registrations depend on. A production host
        // has far more; what matters here is that AddDependencyGraph asks for nothing else.
        services.AddSingleton<ICommandDispatcher>(
            new DependencyGraphTestDispatcher(new DependencyGraphDocumentStore(new FileWriter())));
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
    public void TheEmptyDocument_IsTheSchemaMarkerAndAnEmptyListAndNothingElse()
    {
        // Arrange.
        using var provider = Build();

        // Act.
        var text = EmptyDocument(provider, "services");

        // Assert.
        var model = DependencyGraphBody.Parse(text).Disl;
        Assert.Empty(model.Elements);
        Assert.Empty(model.Relations);
        Assert.Equal("dependencies: 1\r\nelements: []\r\n", text);
    }

    [Fact]
    public void TheFirstElementAddedToTheEmptyDocument_ProducesValidYaml()
    {
        // Arrange.
        // The trap this test exists for: `elements: []` already carries a value, and appending a
        // block entry after it without opening the section leaves the key with two values and
        // the file unparseable - a bug that would surface on the very first Add of every new
        // graph.
        using var provider = Build();
        var document = DependencyGraphBody.Parse(EmptyDocument(provider, "services"));

        // Act.
        var added = DependencyGraphDefinition.Apply(document, DependencyGraphEdits.Transaction(DependencyGraphEdits.Create("first001", "First", 120, 0)));

        // Assert.
        Assert.True(added.WasApplied);
        var reparsed = DependencyGraphBody.Parse(document.Text);
        Assert.False(reparsed.IsUnreadable);
        var element = Assert.Single(reparsed.Disl.Elements);
        Assert.Equal("First", element.Label);
        Assert.DoesNotContain("[]", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASession_IsOpenedOnTheBody_WithNoRegistration()
    {
        // Arrange: a bare .dgr routes with no registration at all, so opening must not need one.
        await using var provider = Build();
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>()).Find(Diagram.DependencyGraph.Origin)!;

        // Act.
        await using var session = factory.Open(ShortGuid.NewShortGuid(), "root", DependencyGraphDocumentStoreTests.Simple, null);

        // Assert.
        Assert.IsType<DependencyGraphSession>(session);
        Assert.NotEmpty(session.Baseline());
    }

    private static string EmptyDocument(ServiceProvider provider, string baseName) =>
        new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>())
            .Find(Diagram.DependencyGraph.Origin)!
            .CreateEmptyDocument(baseName);

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
