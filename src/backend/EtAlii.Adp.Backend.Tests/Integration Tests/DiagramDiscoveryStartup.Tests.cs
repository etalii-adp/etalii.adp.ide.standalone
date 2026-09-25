using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Proves discovery against the real host and the real deployment: the host's startup runs
/// the seeded assembly walk, and the diagram modules it carries - which its compiled metadata
/// never references - end up in <see cref="DiagramDefinitionCatalog"/>. This is the test that
/// would have failed with an unseeded walk, which returns nothing here.
/// </summary>
public class DiagramDiscoveryStartupTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;

    public DiagramDiscoveryStartupTests(WebApplicationFactory<Program> factory)
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
                    provider.GetRequiredService<DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public void AfterStartup_AllHoldsTheDeployedDiagramTypes()
    {
        // Act.
        // Building the server is what runs Program.cs, and with it discovery. The cache is
        // per process and other test classes build hosts too, so this reads it rather than
        // calling Initialize - whichever host ran first filled it from the same deployment.
        using var _ = _factory.CreateClient();

        // Assert.
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        Assert.NotEmpty(catalog.All);

        // A scaffolded module known to declare a Definition, found by origin not by type -
        // this test must not reference any module.
        Assert.Contains(catalog.All, definition => definition.Origin.Key == "c4/context");
    }

    [Fact]
    public void AfterStartup_AllIsOrderedByOriginAndFreeOfDuplicates()
    {
        // Arrange.
        using var _ = _factory.CreateClient();

        // Act.
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        var origins = catalog.All.Select(definition => definition.Origin).ToList();

        // Assert.
        // Ordered the way discovery orders: vendor, then type, then subtype - which keeps a
        // vendor's notations together, and is what the Add dialog's grouping relies on.
        //
        // This used to sort the full "vendor/type" key ordinally instead, which gave the same
        // answer for every type then deployed and is not the same rule. The two disagree as
        // soon as one vendor's name is a prefix of another's followed by a character below '/'
        // - `azure` and `azure-devops` are the first such pair, since '-' (0x2D) sorts before
        // '/' (0x2F), so the full-key sort would demand azure-devops/pipeline before
        // azure/architecture while discovery puts the azure vendor first. Discovery is right
        // and the old assertion was a coincidence.
        Assert.Equal(
            origins
                .OrderBy(origin => origin.Vendor, StringComparer.Ordinal)
                .ThenBy(origin => origin.Type, StringComparer.Ordinal)
                .ThenBy(origin => origin.Subtype, StringComparer.Ordinal)
                .Select(origin => origin.Key),
            origins.Select(origin => origin.Key));

        var keys = origins.Select(origin => origin.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AfterStartup_EveryDefinitionEveryDeployedModuleDeclaresIsFound()
    {
        // Arrange.
        // Every EtAlii.Adp.Diagram.* assembly deployed beside the host is a module, and each
        // declares one or more definitions through its own Diagram.Definitions array. Reading
        // those arrays pins this test to the deployment rather than to a number that goes stale
        // as modules are added - and it counts definitions rather than assemblies, which is not
        // the same thing since C4 carries seven notations in one module.
        using var _ = _factory.CreateClient();

        // Act.
        var declared = Directory.GetFiles(AppContext.BaseDirectory, "EtAlii.Adp.Diagram.*.dll")
            .Select(IoPath.GetFileNameWithoutExtension)
            .Where(name => name is not null && !name.EndsWith(".Tests", StringComparison.Ordinal))
            .Select(name => Assembly.Load(name!))
            .Select(assembly => assembly.GetTypes().FirstOrDefault(type => type.Name == "Diagram"))
            .Where(type => type is not null)
            .SelectMany(type => (DiagramDefinition[])type!.GetProperty("Definitions")!.GetValue(null)!)
            .Select(definition => definition.Origin.Key)
            .ToList();
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();

        // Assert.
        // What this test is for is that nothing deployed goes missing, so it compares the two
        // as sets. Ordering is a separate promise with its own test, and asserting it here too
        // meant restating discovery's sort rule in a second place - where it was restated
        // wrongly, and only stayed correct while no two vendors shared a prefix.
        Assert.NotEmpty(declared);
        Assert.Equal(
            declared.OrderBy(key => key, StringComparer.Ordinal),
            catalog.All.Select(definition => definition.Origin.Key).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void AfterStartup_TheModuleCarryingSeveralNotations_ContributesAllOfThem()
    {
        // Arrange.
        // C4 is the reason discovery reads an array. One module, seven notations - and a
        // regression to a singular read would show up here as one of them rather than all.
        using var _ = _factory.CreateClient();

        // Act.
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        var c4 = catalog.All
            .Where(definition => definition.Origin.Vendor == "c4")
            .Select(definition => definition.Origin.Key)
            .ToList();

        // Assert.
        Assert.Equal(
            ["c4/code", "c4/component", "c4/container", "c4/context", "c4/deployment", "c4/dynamic", "c4/system-landscape"],
            c4);
    }

    [Fact]
    public void AfterStartup_TheFunctionalDecompositionGraphIsCataloged()
    {
        // Arrange.
        // FDG task 11's guard (the user's chat ruling of 2026-09-25). It names the origin because the
        // every-module comparison above cannot catch this: a module that declares NO definitions is
        // missing from both of its sides and the sets still match. FDG's assembly was deployed beside
        // the host from task 9 onwards and declared none, so that test was green the whole time FDG
        // could not be opened. The key is a string rather than the module's own constant so that
        // removing the definition fails this test when it runs, instead of breaking the build.
        using var _ = _factory.CreateClient();

        // Act.
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();

        // Assert.
        Assert.Contains("etalii/functional-decomposition-graph", catalog.All.Select(definition => definition.Origin.Key));
    }

    [Fact]
    public void AfterStartup_EveryDeployedDefinitionCarriesADescription()
    {
        // Arrange.
        // Every discovered type describes itself, because the Add dialog shows that description
        // when a user selects the type. A module shipping without one leaves a blank panel,
        // which is worse than no panel at all. Checked here, against the real deployed set,
        // because this is the one place that sees every module without core naming any of them.
        using var _ = _factory.CreateClient();

        // Act.
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        var undescribed = catalog.All.Where(definition => string.IsNullOrWhiteSpace(definition.Description)).ToList();

        // Assert.
        Assert.Empty(undescribed);
    }

    [Fact]
    public void AfterStartup_NoDeployedDescriptionMerelyEchoesItsTitle()
    {
        // Arrange: a description that merely repeats the title tells a user nothing new.
        using var _ = _factory.CreateClient();

        // Act.
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        var echoes = catalog.All
            .Where(definition => string.Equals(definition.Description.Trim(), definition.Title.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Assert.
        Assert.Empty(echoes);
    }

    [Fact]
    public void AfterStartup_EveryDefinitionWithoutItsOwnRegistrationIsCataloged()
    {
        // Arrange.
        // These origin tags used to be asserted in 48 per-module test files, all structurally
        // identical; this is their one home now, against the live deployed catalog, beside the
        // description checks that made the same move earlier. The population is selected
        // structurally - no Build delegate of its own - so it needs no hand-kept list, and the
        // definitions that do register services (whose docs row may legitimately annotate
        // further) stay out of it.
        //
        // "No Build delegate" is NOT the same as "a stub", and the difference matters to anyone
        // extending this. A family registers its engine once and loops over its origins, so
        // c4's Container, Component, Deployment, Dynamic and SystemLandscape and databricks'
        // Job and Pipeline all carry no Build of their own and are all fully served - a session
        // factory, document factory and toolbox provider are registered per origin inside
        // AddC4() and AddDatabricks(). They belong in this population for the same reason real
        // stubs do (their catalog row carries the plain title), but calling them stubs is
        // wrong, and a reader who trusts that name concludes seven shipped diagram types are
        // unimplemented.
        using var _ = _factory.CreateClient();

        // Act.
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        var cataloged = CatalogedTitles();
        var missing = catalog.All
            .Where(definition => definition.Build is null)
            .Where(definition => !cataloged.ContainsKey(definition.Origin.Key))
            .Select(definition => $"{definition.Origin.Key}: not cataloged in docs/diagrams.md")
            .ToList();

        // Assert.
        Assert.Empty(missing);
    }

    [Fact]
    public void AfterStartup_EveryDefinitionWithoutItsOwnRegistrationMatchesItsCatalogedName()
    {
        // Arrange.
        using var _ = _factory.CreateClient();

        // Act.
        var catalog = _factory.Services.GetRequiredService<IDiagramDefinitionCatalog>();
        var cataloged = CatalogedTitles();
        var mismatched = catalog.All
            .Where(definition => definition.Build is null)
            .Where(definition => cataloged.TryGetValue(definition.Origin.Key, out var expected)
                && !string.Equals(definition.Title, expected, StringComparison.Ordinal))
            .Select(definition =>
                $"{definition.Origin.Key}: definition title '{definition.Title}' does not match docs/diagrams.md's '{cataloged[definition.Origin.Key]}'")
            .ToList();

        // Assert.
        // A failure names the offending origin and both strings, so this collapsed check is no
        // harder to diagnose than the 48 files it replaces (technical-debt-cleanup R4.2).
        Assert.Empty(mismatched);
    }

    /// <summary>
    /// The diagram catalog table, origin tag to Diagram-column title, read from the repository's
    /// own docs/diagrams.md - the source of truth the module doc-comments already point at.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CatalogedTitles()
    {
        // The catalog carries rows in two shapes: plain markdown pipe rows, and - since the
        // section tables were combined into one HTML table for rendering - <tr> rows whose
        // origin sits in a <code> cell. Both are read: the document's format is the document's
        // own business, and this parser follows it rather than pinning it. (The HTML shape
        // going unparsed is exactly how 48 stub origins silently fell out of this check once.)
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(LocateCatalog()))
        {
            if (line.StartsWith('|'))
            {
                var cells = line.Split('|');
                if (cells.Length < 5)
                {
                    continue;
                }

                var origin = cells[2].Trim();
                if (origin.Length < 3 || origin[0] != '`' || origin[^1] != '`')
                {
                    continue;
                }

                titles[origin[1..^1]] = cells[3].Trim();
                continue;
            }

            if (!line.Contains("<tr>", StringComparison.Ordinal))
            {
                continue;
            }

            var htmlCells = Regex.Matches(line, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline);
            if (htmlCells.Count < 3)
            {
                continue;
            }

            var origin2 = Regex.Match(htmlCells[1].Groups[1].Value, "<code>([^<]+)</code>");
            if (!origin2.Success)
            {
                continue;
            }

            titles[origin2.Groups[1].Value.Trim()] = HtmlCellText(htmlCells[2].Groups[1].Value);
        }

        return titles;
    }

    /// <summary>A cell's visible text: tags stripped, entities decoded, non-breaking spaces ordinary again.</summary>
    private static string HtmlCellText(string cell) =>
        WebUtility.HtmlDecode(Regex.Replace(cell, "<[^>]+>", "")).Replace('\u00A0', ' ').Trim();

    /// <summary>
    /// The catalog document, found by walking up from the test binary rather than by counting
    /// `..` segments - the count changes with the build layout, the path does not.
    /// </summary>
    private static string LocateCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "docs", "diagrams.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("docs/diagrams.md could not be found from " + AppContext.BaseDirectory);
    }
}
