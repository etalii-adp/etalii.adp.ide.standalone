using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Editor;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Which editor a file resolves to (modular-text-editors Requirements 2.2, 2.3, 3.2, 4.2,
/// 4.3, 4.5), over a stub catalog - no host, no gRPC, per the Non-Functional Testing
/// requirement. The one behaviour most worth guarding is the failure mode: a conflict logs
/// once at Error and degrades per file; it never throws, because an editor clash must not
/// take diagrams down with it.
/// </summary>
[Collection(LogCapture.Collection)]
public class EditorResolverTests : IDisposable
{
    private readonly LogCapture _logger = LogCapture.Start();

    public void Dispose() => _logger.Dispose();

    private sealed class StubEditorCatalog(params EditorDefinition[] definitions) : IEditorDefinitionCatalog
    {
        public IReadOnlyList<EditorDefinition> All { get; } = definitions;
    }

    private static readonly EditorDefinition Plain = new("plain", "Plain Text", IsFallback: true);
    private static readonly EditorDefinition Markdown = new("markdown", "Markdown", Extensions: [".md"]);

    [Fact]
    public void Resolve_WithOneClaimant_RoutesToIt()
    {
        // Arrange.
        var resolver = new EditorResolver(new StubEditorCatalog(Plain, Markdown));

        // Act.
        var routing = resolver.Resolve(@"C:\project\readme.md");

        // Assert.
        var routed = Assert.IsType<EditorRouted>(routing);
        Assert.Equal("markdown", routed.Definition.Id);
    }

    [Fact]
    public void Resolve_WithNoClaimant_FallsBackByConstruction()
    {
        // Arrange: nothing claims .xyz - the fallback is what remains (Requirement 3.2).
        var resolver = new EditorResolver(new StubEditorCatalog(Plain, Markdown));

        // Act.
        var routing = resolver.Resolve(@"C:\project\data.xyz");

        // Assert.
        var routed = Assert.IsType<EditorRouted>(routing);
        Assert.Equal("plain", routed.Definition.Id);
    }

    [Fact]
    public void Resolve_IsCaseInsensitiveOnTheExtension()
    {
        // Arrange (Requirement 2.2: ".MD" on disk is ".md"'s claim).
        var resolver = new EditorResolver(new StubEditorCatalog(Plain, Markdown));

        // Act.
        var routing = resolver.Resolve(@"C:\project\README.MD");

        // Assert.
        var routed = Assert.IsType<EditorRouted>(routing);
        Assert.Equal("markdown", routed.Definition.Id);
    }

    [Fact]
    public void Resolve_MatchesAnExtensionlessFileByItsExactName()
    {
        // Arrange (Requirement 2.3).
        var make = new EditorDefinition("make", "Makefiles", FileNames: ["Makefile"]);
        var resolver = new EditorResolver(new StubEditorCatalog(Plain, make));

        // Act.
        var routing = resolver.Resolve(@"C:\project\Makefile");

        // Assert.
        var routed = Assert.IsType<EditorRouted>(routing);
        Assert.Equal("make", routed.Definition.Id);
    }

    [Fact]
    public void Resolve_AmbiguousWithADeclaredDefault_RoutesToTheDefault()
    {
        // Arrange (Requirement 4.4's legitimate case).
        var one = new EditorDefinition("one", "One", Extensions: [".md"]);
        var two = new EditorDefinition("two", "Two", Extensions: [".md"], IsDefaultForSharedExtension: true);
        var resolver = new EditorResolver(new StubEditorCatalog(Plain, one, two));

        // Act.
        var routing = resolver.Resolve(@"C:\project\readme.md");

        // Assert.
        var routed = Assert.IsType<EditorRouted>(routing);
        Assert.Equal("two", routed.Definition.Id);
    }

    [Fact]
    public void Resolve_AmbiguousWithoutADefault_DegradesPerFileAndLogsOnceAtError()
    {
        // Arrange: the corrected R4 precedent - the conflict is a startup Error, each file
        // reports it, and CONSTRUCTION DOES NOT THROW even though DiagramValidators's
        // familiar pattern would (Requirements 4.2, 4.3, 4.5).
        var one = new EditorDefinition("one", "One", Extensions: [".md"]);
        var two = new EditorDefinition("two", "Two", Extensions: [".md"]);

        // Act.
        var resolver = new EditorResolver(new StubEditorCatalog(Plain, one, two));
        var routing = resolver.Resolve(@"C:\project\readme.md");

        // Assert: the result names the conflict, and the Error line was written - once, at
        // construction, not per resolve. Filtered to this resolver's own line: other Backend
        // test classes log in parallel without joining the capture collection, so a global
        // count would race their events.
        var ambiguous = Assert.IsType<EditorAmbiguous>(routing);
        Assert.Equal(".md", ambiguous.Extension);
        Assert.Equal(new[] { "one", "two" }, ambiguous.Claimants.Select(claimant => claimant.Id));
        var error = Assert.Single(_logger.Errors, line => line.Contains("one, two"));
        Assert.Contains(".md", error);

        resolver.Resolve(@"C:\project\other.md");
        Assert.Single(_logger.Errors, line => line.Contains("one, two"));
    }

    [Fact]
    public void Resolve_WithNoFallbackDeployed_ReportsRatherThanCrashes()
    {
        // Arrange: a broken deployment - one Error at startup, degraded results, no throw.
        var resolver = new EditorResolver(new StubEditorCatalog(Markdown));

        // Act.
        var routing = resolver.Resolve(@"C:\project\data.xyz");

        // Assert.
        Assert.IsType<EditorAmbiguous>(routing);
        Assert.Contains(_logger.Errors, error => error.Contains("IsFallback"));
    }
}
