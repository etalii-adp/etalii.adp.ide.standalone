using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Every tracked example registration under <c>src/diagrams/*/examples/</c>, opened against the
/// real deployed catalog. Before this test existed, 26 of the 39 example registrations were
/// opened by nothing at all - only c4's two sets had coverage - so a rename of the example
/// material had a safety net for a third of it (adp-file-nesting Requirements 11.1 and 12.3).
/// </summary>
/// <remarks>
/// Written against the CURRENT names, before any rename, deliberately: a test written after a
/// rename asserts that the rename did what it did, while this one asserts the rename did not
/// change what these files mean. The walk is live rather than a hard-coded list, so an example
/// set a later spec adds is covered on arrival.
/// </remarks>
public class ExampleRegistrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ExampleRegistrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("developer"));
    }

    /// <summary>
    /// The `src/diagrams` folder, found by walking up from the test binary rather than by
    /// counting `..` segments - the count changes with the build layout, the folder name
    /// does not.
    /// </summary>
    private static string DiagramsRoot { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "diagrams");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The src/diagrams folder was not found above the test binary.");
    }

    public static TheoryData<string> EveryExampleRegistration()
    {
        var data = new TheoryData<string>();
        foreach (var module in Directory.EnumerateDirectories(Locate()))
        {
            var examples = IoPath.Combine(module, "examples");
            if (!Directory.Exists(examples))
            {
                continue;
            }

            foreach (var adp in Directory.EnumerateFiles(examples, "*.adp", SearchOption.AllDirectories))
            {
                data.Add(IoPath.GetRelativePath(Locate(), adp));
            }
        }

        return data;
    }

    [Fact]
    public void TheWalk_FindsTheTrackedExampleSet()
    {
        // Arrange and act.
        var found = EveryExampleRegistration().Count();

        // Assert: 39 tracked registrations at the time of writing. This count moving is fine -
        // it moving DOWN unexpectedly is what this fact is here to catch, since a vanished
        // example silently shrinks the theory below rather than failing it.
        Assert.True(found >= 39, $"only {found} example registrations found; the walk lost some");
    }

    [Theory]
    [MemberData(nameof(EveryExampleRegistration))]
    public void EveryExampleRegistration_ResolvesInTheDeployedCatalog(string relativePath)
    {
        // Arrange.
        // Building the server runs Program.cs and with it discovery, so the catalog holds the
        // real deployed set.
        using var _ = _factory.CreateClient();
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        var adpPath = IoPath.Combine(DiagramsRoot, relativePath);

        // Act.
        var definition = DiagramFilePair.DefinitionOf(adpPath, catalog);

        // Assert.
        Assert.True(definition is not null, $"{relativePath} names a MIME type the deployed catalog does not carry");
    }

    [Theory]
    [MemberData(nameof(EveryExampleRegistration))]
    public void EveryExampleRegistrationWithABody_ResolvesToAFileThatExists(string relativePath)
    {
        // Arrange.
        using var _ = _factory.CreateClient();
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        var adpPath = IoPath.Combine(DiagramsRoot, relativePath);
        var definition = DiagramFilePair.DefinitionOf(adpPath, catalog);
        if (definition is not { HasDocumentSibling: true })
        {
            // A type that keeps no body - c4/code among them - has nothing to resolve, and that
            // is its correct behaviour, not a gap (the HasDocumentSibling branch, deliberately
            // distinct from the unknown-MIME branch).
            return;
        }

        // Act.
        // A body: header resolves against the example set's own root - the directory directly
        // under examples/ - which is the project a user would open.
        var body = DiagramFilePair.BodyOf(adpPath, catalog, ExampleRootOf(adpPath));

        // Assert.
        Assert.True(body is not null, $"{relativePath} resolves no body although its type keeps one");
        Assert.True(File.Exists(body.Value.Path), $"{relativePath} resolves a body that does not exist: {body.Value.Path}");
    }

    /// <summary>
    /// Every qualified example registration carries an explicit <c>body:</c> header
    /// (adp-file-nesting Requirement 2.3 and the rename task's own success criterion): the
    /// header is authoritative, and a qualified registration ADP authors states its body as a
    /// fact rather than leaving it to derivation. Replacing a fact with an inference is a
    /// downgrade even where the inference is currently right.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryExampleRegistration))]
    public void EveryQualifiedExampleRegistration_CarriesAnExplicitBodyHeader(string relativePath)
    {
        // Arrange.
        var adpPath = IoPath.Combine(DiagramsRoot, relativePath);
        var name = DiagramRegistrationName.TryParse(IoPath.GetFileName(adpPath));
        if (name is not { IsQualified: true })
        {
            return; // Unqualified and folder-scoped forms derive; only the qualified form must state.
        }

        // Act.
        var lines = File.ReadLines(adpPath).Skip(1).Take(8);

        // Assert.
        Assert.Contains(lines, line => line.TrimStart().StartsWith("body:", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The example set's root: the child of the module's <c>examples/</c> folder the file sits under.</summary>
    private static string ExampleRootOf(string adpPath)
    {
        for (var directory = new DirectoryInfo(adpPath).Parent; directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.Parent?.Name, "examples", StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"{adpPath} does not sit under an examples/ set.");
    }
}
