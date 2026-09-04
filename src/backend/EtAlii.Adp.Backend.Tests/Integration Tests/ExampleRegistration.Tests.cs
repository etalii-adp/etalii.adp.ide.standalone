using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
public class ExampleRegistrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;

    public ExampleRegistrationTests(WebApplicationFactory<Program> factory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_appDataRoot);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                // The problem cache must live and die with this test, not in the real user
                // profile the host's AddProblems registration points at. A host booted without
                // this override leaves a cache file behind naming a temp folder that is deleted
                // moments later, and every later run then walks that dead root at startup: 605
                // such files had accumulated, costing the suite 22,591 warnings and an apparent
                // hang. DiagramToolboxFlowTests fixed this for itself; it never generalised.
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<Diagram.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
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

    /// <summary>
    /// The example roots this suite walks: every module's own <c>examples/</c> folder, and
    /// <c>src/examples</c> - the tree a user actually opens as a project. The latter joined
    /// after adp-file-nesting shipped a regression that only showed there: the module trees
    /// were covered, the user-facing one was not, and "the tests are green" meant less than
    /// it appeared to.
    /// </summary>
    private static IEnumerable<string> ExampleRoots()
    {
        foreach (var module in Directory.EnumerateDirectories(Locate()))
        {
            var examples = IoPath.Combine(module, "examples");
            if (Directory.Exists(examples))
            {
                yield return examples;
            }
        }

        var userFacing = IoPath.Combine(IoPath.GetDirectoryName(Locate())!, "examples");
        if (Directory.Exists(userFacing))
        {
            yield return userFacing;
        }
    }

    public static TheoryData<string> EveryExampleRegistration()
    {
        var data = new TheoryData<string>();
        foreach (var examples in ExampleRoots())
        {
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

    public static TheoryData<string> EveryExampleDirectory()
    {
        var data = new TheoryData<string>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var examples in ExampleRoots())
        {
            foreach (var adp in Directory.EnumerateFiles(examples, "*.adp", SearchOption.AllDirectories))
            {
                var directory = IoPath.GetDirectoryName(adp);
                if (directory is not null && directories.Add(directory))
                {
                    data.Add(IoPath.GetRelativePath(Locate(), directory));
                }
            }
        }

        return data;
    }

    /// <summary>
    /// The nesting decision over every shipped example folder (adp-file-nesting Requirement 3,
    /// asserted post-rename per task 18): each registration whose body lives beside it nests
    /// under that subject, nothing shipped is an orphan, and a bodyless type's registration -
    /// code-level.adp - stays an unnested entry rather than becoming one.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryExampleDirectory))]
    public void EveryExampleDirectory_NestsEachRegistrationUnderTheSubjectItNames(string relativeDirectory)
    {
        // Arrange.
        using var _ = _factory.CreateClient();
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        var directory = IoPath.Combine(DiagramsRoot, relativeDirectory);
        var siblings = Directory.EnumerateFileSystemEntries(directory)
            .Select(entry => (Name: IoPath.GetFileName(entry), IsFolder: Directory.Exists(entry)))
            .ToList();

        string? ResolveBody(string fileName)
        {
            var adpPath = IoPath.Combine(directory, fileName);
            var body = DiagramFilePair.BodyOf(adpPath, catalog, ExampleRootOf(adpPath));
            if (body is not { } resolved || resolved.Path.Length == 0)
            {
                return null;
            }

            return string.Equals(IoPath.GetDirectoryName(resolved.Path), directory, StringComparison.OrdinalIgnoreCase)
                ? IoPath.GetFileName(resolved.Path)
                : null;
        }

        // Act.
        var placements = HierarchyNesting.Assign(siblings, ResolveBody);

        // Assert.
        foreach (var placement in placements)
        {
            Assert.False(placement.IsOrphan, $"{relativeDirectory}/{placement.Name} is an orphan: its subject is missing");

            if (string.Equals(placement.Name, "code-level.adp", StringComparison.OrdinalIgnoreCase))
            {
                // The bodyless known type: found in the catalog, keeps no body, nests nowhere.
                Assert.Null(placement.SubjectName);
                continue;
            }

            var body = ResolveBody(placement.Name);
            if (body is not null)
            {
                Assert.Equal(body, placement.SubjectName);
            }
        }
    }

    /// <summary>
    /// The unqualified form leads every shipped registration set (Requirements 3.4 and 7.2):
    /// it is the diagram a subject's activation opens, so its place at the front is behaviour,
    /// not cosmetics.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryExampleDirectory))]
    public void EveryExampleRegistrationSet_LeadsWithTheUnqualifiedForm(string relativeDirectory)
    {
        // Arrange.
        var directory = IoPath.Combine(DiagramsRoot, relativeDirectory);
        var registrations = Directory.EnumerateFiles(directory, "*.adp")
            .Select(IoPath.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToList();

        var bySubject = registrations
            .Select(name => DiagramRegistrationName.TryParse(name))
            .Where(parsed => parsed is not null && !parsed.IsFolderScoped)
            .GroupBy(parsed => parsed!.SubjectBase, StringComparer.OrdinalIgnoreCase);

        foreach (var group in bySubject)
        {
            var set = group.Select(parsed => parsed!.IsQualified ? $"{parsed.SubjectBase}.{parsed.Qualifier}.adp" : $"{parsed.SubjectBase}.adp").ToList();
            if (set.Count < 2)
            {
                continue;
            }

            // Act.
            set.Sort(HierarchyNesting.CompareRegistrations);

            // Assert.
            var first = DiagramRegistrationName.TryParse(set[0]);
            Assert.NotNull(first);
            Assert.False(first.IsQualified, $"{relativeDirectory}: '{set[0]}' leads the set but is qualified");
        }
    }

    /// <summary>
    /// The five header-pointed registrations kept their headers through the rename, and the
    /// header is still what resolves - authoritative, not decorative (Requirement 2.3).
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryExampleRegistration))]
    public void EveryQualifiedExampleRegistrationsHeader_IsStillAuthoritative(string relativePath)
    {
        // Arrange.
        using var _ = _factory.CreateClient();
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        var adpPath = IoPath.Combine(DiagramsRoot, relativePath);
        var name = DiagramRegistrationName.TryParse(IoPath.GetFileName(adpPath));
        if (name is not { IsQualified: true })
        {
            return;
        }

        // Act: what the header names is what opens, and a header-resolved body is never owned.
        var body = DiagramFilePair.BodyOf(adpPath, catalog, ExampleRootOf(adpPath));

        // Assert.
        Assert.NotNull(body);
        Assert.False(body.Value.IsOwned, $"{relativePath}: a header-pointed body must never be carried off by one registration");
        Assert.True(File.Exists(body.Value.Path));
    }

    /// <summary>
    /// The example set's root - the folder a user would open as a project. Two layouts exist:
    /// a module's <c>examples/&lt;set&gt;/...</c>, where the set is the child of examples, and
    /// the user-facing <c>src/examples/diagrams/&lt;type&gt;/&lt;set&gt;/...</c>, where it sits
    /// below the type. Getting this wrong resolves every body: header against the wrong root,
    /// which is how this helper mis-flagged ten healthy registrations when src/examples first
    /// joined the walk - and mis-flagged ten c4 ones again when the diagrams/ level was added.
    /// </summary>
    private static string ExampleRootOf(string adpPath)
    {
        string? previous = null;
        for (var directory = new DirectoryInfo(adpPath).Parent; directory is not null; directory = directory.Parent)
        {
            // The user-facing layout: this directory is the <type> under src/examples/diagrams,
            // and the set is its child on the walked path - or the type folder itself, for a
            // registration sitting directly in it.
            if (string.Equals(directory.Parent?.Name, "diagrams", StringComparison.OrdinalIgnoreCase)
                && string.Equals(directory.Parent!.Parent?.Name, "examples", StringComparison.OrdinalIgnoreCase)
                && string.Equals(directory.Parent!.Parent?.Parent?.Name, "src", StringComparison.OrdinalIgnoreCase))
            {
                return previous ?? directory.FullName;
            }

            // The module layout: examples/<set>, with examples sitting inside the module rather
            // than under src.
            if (string.Equals(directory.Parent?.Name, "examples", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(directory.Parent!.Parent?.Name, "src", StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }

            previous = directory.FullName;
        }

        throw new InvalidOperationException($"{adpPath} does not sit under an examples/ set.");
    }
}
