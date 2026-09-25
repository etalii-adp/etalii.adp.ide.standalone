using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// The module's one registration point.
/// </summary>
/// <remarks>
/// Requirement 14.3 and 14.4 are the pluggability claims: the host names this module once and
/// nothing in core names it back. Both are the kind of claim that stays true only while something
/// checks it, since a single stray <c>using</c> in a core file would quietly falsify the second
/// one and nothing else would notice.
/// </remarks>
public class ServiceCollectionAddAzurePipelineTests
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
        typeof(IPipelineDocumentStore),
    ];

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // The few core services this module's own registrations depend on. A production host has
        // far more; what matters here is that AddAzurePipeline asks for nothing else.
        services.AddSingleton<ICommandDispatcher>(new PipelineTestDispatcher(new PipelineDocumentStore()));
        services.AddSingleton<IHistoryStackStore>(provider =>
            new HistoryStackStore(provider.GetRequiredService<ICommandDispatcher>()));
        services.AddSingleton<IDiagramDefinitionCatalog, PipelineOnlyCatalog>();
        services.AddSingleton<DiagramFileRouter>();

        services.AddAzurePipeline();
        return services.BuildServiceProvider();
    }

    [Theory]
    [MemberData(nameof(RegisteredSeams))]
    public void OneCall_FillsEverySeamTheModuleUses(Type seam)
    {
        // Arrange & act.
        using var provider = Build();

        // Assert.
        // Resolved rather than merely present in the collection, so a registration whose
        // dependencies cannot be satisfied fails here rather than at the first request.
        Assert.NotNull(provider.GetService(seam));
    }

    [Fact]
    public void EveryCommandTheModuleDefines_HasItsHandlerRegistered()
    {
        // Arrange: a command with no handler throws at dispatch, which is the worst place to
        // find out. The list comes from the assembly rather than from a second hand-written list,
        // so adding a command without registering it fails here.
        using var provider = Build();
        var commands = typeof(RenamePipelineElementCommand).Assembly
            .GetTypes()
            .Where(type => typeof(ICommand).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
            .ToList();

        // Assert.
        Assert.NotEmpty(commands);
        foreach (var command in commands)
        {
            var handler = typeof(ICommandHandler<>).MakeGenericType(command);
            Assert.True(provider.GetService(handler) is not null, $"{command.Name} has no registered handler.");
        }
    }

    [Fact]
    public void TheSessionFactory_AnswersForThisModulesOrigin()
    {
        // Arrange: the session factory is resolved by origin, so a mismatch is a diagram that
        // opens to nothing rather than an error anybody sees.
        using var provider = Build();

        // Act.
        var factory = provider.GetRequiredService<IDiagramSessionFactory>();

        // Assert.
        Assert.Equal(Diagram.Pipeline.Origin, factory.Origin);
    }

    [Fact]
    public void TheProvidersAnswerForTheDiagramElementScope()
    {
        // Arrange & act.
        using var provider = Build();

        // Assert.
        Assert.Equal(ContextScope.DiagramElement, provider.GetRequiredService<IContextActionProvider>().Scope);
        Assert.Equal(ContextScope.DiagramElement, provider.GetRequiredService<IContextPropertyProvider>().Scope);
    }

    [Fact]
    public void TheDocumentStore_IsSharedAcrossEverythingThatUsesIt()
    {
        // Arrange: two diagrams on one pipeline have to see one document, which only holds if the
        // session, the resolver and the providers were all handed the same instance.
        using var provider = Build();

        // Act.
        var first = provider.GetRequiredService<IPipelineDocumentStore>();
        var second = provider.GetRequiredService<IPipelineDocumentStore>();

        // Assert.
        Assert.Same(first, second);
    }

    [Fact]
    public void CoreNamesNothingFromThisModule()
    {
        // Arrange: Requirement 14.4 - core must compile and run with this module absent, which
        // stops being true the moment a core file mentions a type from it. Checked against the
        // compiled assemblies rather than by reading source, so a reference through any path
        // counts.
        var module = typeof(Diagram).Assembly.GetName().Name!;

        // Act.
        var offenders = new[] { typeof(IDiagramSession).Assembly, typeof(DiagramDefinition).Assembly }
            .Where(core => core.GetReferencedAssemblies().Any(reference => reference.Name == module))
            .Select(core => core.GetName().Name)
            .ToList();

        // Assert.
        Assert.Empty(offenders);
    }

    [Fact]
    public void TheModuleRegistersItselfExactlyOnce()
    {
        // Arrange: Requirement 14.3 - the module is named once rather than listing its seams. A
        // second call would double every singleton registration, and a seam registered twice is
        // a provider consulted twice. Since the AddDiagramDefinitions refactor, the host no
        // longer calls any diagram module by name (see TheHostCallsNoIndividualModule below);
        // each module's own Diagram.cs supplies a Build delegate instead, invoked once per
        // discovered definition - so "named once" is now a claim about that file.
        var diagram = File.ReadAllText(ModuleDiagramPath());

        // Act.
        var calls = diagram.Split("AddAzurePipeline(").Length - 1;

        // Assert.
        Assert.Equal(1, calls);
    }

    [Fact]
    public void TheHostCallsNoIndividualModule()
    {
        // Arrange: the other half of Requirement 14.3 under the current architecture - the host
        // discovers and builds every module generically through AddDiagramDefinitions, so
        // Program.cs must not call this module by name either.
        var program = File.ReadAllText(HostProgramPath());

        // Act & assert.
        Assert.DoesNotContain("AddAzurePipeline(", program, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHostNamesNoIndividualSeamOfThisModule()
    {
        // Arrange: the other half of "one line" - Program.cs must not know what is inside.
        var program = File.ReadAllText(HostProgramPath());

        // Act.
        var leaked = new[]
        {
            nameof(PipelineDocumentFactory),
            nameof(PipelineSessionFactory),
            nameof(PipelineContextSourceResolver),
            nameof(PipelineContextActionProvider),
            nameof(PipelineContextPropertyProvider),
            nameof(PipelineToolboxProvider),
            nameof(PipelineDocumentStore),
        }.Where(name => program.Contains(name, StringComparison.Ordinal)).ToList();

        // Assert.
        Assert.Empty(leaked);
    }

    /// <summary>
    /// The host's <c>Program.cs</c>, found from the test binary rather than hard-coded.
    /// </summary>
    /// <remarks>
    /// Walks up to the <c>src</c> folder, so the path holds wherever the repository is checked out
    /// and whichever worktree this is running in.
    /// </remarks>
    private static string HostProgramPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !string.Equals(directory.Name, "src", StringComparison.OrdinalIgnoreCase))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var program = System.IO.Path.Combine(directory.FullName, "backend", "EtAlii.Adp.Backend.Service", "Program.cs");
        Assert.True(File.Exists(program), $"The host's Program.cs was not found at {program}.");
        return program;
    }

    /// <summary>
    /// This module's own <c>Diagram.cs</c>, found from the test binary rather than hard-coded -
    /// see the remarks on <see cref="HostProgramPath"/>.
    /// </summary>
    private static string ModuleDiagramPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !string.Equals(directory.Name, "src", StringComparison.OrdinalIgnoreCase))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var diagram = System.IO.Path.Combine(
            directory.FullName, "diagrams", "azure-pipeline", "backend", "EtAlii.Adp.Diagram.AzurePipeline", "Diagram.cs");
        Assert.True(File.Exists(diagram), $"The module's Diagram.cs was not found at {diagram}.");
        return diagram;
    }
}
