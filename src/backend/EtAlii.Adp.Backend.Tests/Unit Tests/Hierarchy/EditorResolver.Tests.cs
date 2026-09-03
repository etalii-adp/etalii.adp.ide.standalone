using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Editor;
using Serilog;
using Serilog.Events;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Which editor a file resolves to (modular-text-editors Requirements 2.2, 2.3, 3.2, 4.2,
/// 4.3, 4.5), over a stub catalog - no host, no gRPC, per the Non-Functional Testing
/// requirement. The one behaviour most worth guarding is the failure mode: a conflict logs
/// once at Error and degrades per file; it never throws, because an editor clash must not
/// take diagrams down with it.
/// </summary>
public class EditorResolverTests
{
    // A private pipeline per test class, handed straight to the resolver: the SHARED static
    // pipeline is replaced whenever an integration test in this assembly builds a real host,
    // so a capture over it sees these Error lines only when the test ordering cooperates -
    // which is exactly the flakiness this arrangement removes.
    private readonly List<LogEvent> _events = [];

    private EditorResolver Resolver(params EditorDefinition[] definitions) =>
        new(new StubEditorCatalog(definitions),
            new LoggerConfiguration().WriteTo.Sink(new ListSink(_events)).CreateLogger());

    private IEnumerable<string> Errors => _events
        .Where(logEvent => logEvent.Level == LogEventLevel.Error)
        .Select(logEvent => logEvent.RenderMessage());

    private sealed class ListSink(List<LogEvent> events) : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }

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
        var resolver = Resolver(Plain, Markdown);

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
        var resolver = Resolver(Plain, Markdown);

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
        var resolver = Resolver(Plain, Markdown);

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
        var resolver = Resolver(Plain, make);

        // Act. Forward slashes, deliberately: the exact-name match reads the path's file
        // name, and a backslash literal is not a separator on the Linux CI runner.
        var routing = resolver.Resolve("project/Makefile");

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
        var resolver = Resolver(Plain, one, two);

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
        var resolver = Resolver(Plain, one, two);
        var routing = resolver.Resolve(@"C:\project\readme.md");

        // Assert: the result names the conflict, and the Error line was written - once, at
        // construction, not per resolve. Filtered to this resolver's own line: other Backend
        // test classes log in parallel without joining the capture collection, so a global
        // count would race their events.
        var ambiguous = Assert.IsType<EditorAmbiguous>(routing);
        Assert.Equal(".md", ambiguous.Extension);
        Assert.Equal(new[] { "one", "two" }, ambiguous.Claimants.Select(claimant => claimant.Id));
        var error = Assert.Single(Errors, line => line.Contains("one, two"));
        Assert.Contains(".md", error);

        resolver.Resolve(@"C:\project\other.md");
        Assert.Single(Errors, line => line.Contains("one, two"));
    }

    [Fact]
    public void Resolve_WithNoFallbackDeployed_ReportsRatherThanCrashes()
    {
        // Arrange: a broken deployment - one Error at startup, degraded results, no throw.
        var resolver = Resolver(Markdown);

        // Act.
        var routing = resolver.Resolve(@"C:\project\data.xyz");

        // Assert.
        Assert.IsType<EditorAmbiguous>(routing);
        Assert.Contains(Errors, error => error.Contains("IsFallback"));
    }

    [Fact]
    public void ClaimantsOf_ASharedExtension_ListsEveryClaimantInIdOrder()
    {
        // Arrange: the defaulted case - Resolve answers the default alone, while "Open with…"
        // needs the whole field (Requirement 4.4).
        var one = new EditorDefinition("one", "One", Extensions: [".md"]);
        var two = new EditorDefinition("two", "Two", Extensions: [".md"], IsDefaultForSharedExtension: true);
        var resolver = Resolver(Plain, one, two);

        // Act and assert.
        Assert.Equal(["one", "two"], resolver.ClaimantsOf(@"C:\project\readme.md").Select(claimant => claimant.Id));
    }

    [Fact]
    public void ClaimantsOf_AFallbackOnlyFile_IsEmpty()
    {
        // Arrange and act: nothing claims .txt; the fallback answers Resolve but claims
        // nothing by construction, so there is nothing to choose between.
        var resolver = Resolver(Plain, Markdown);

        // Assert.
        Assert.Empty(resolver.ClaimantsOf(@"C:\project\notes.txt"));
    }

    [Fact]
    public void IsClaimed_AnExtensionClaim_IsTrue()
    {
        // Arrange, act and assert.
        Assert.True(Resolver(Plain, Markdown).IsClaimed(@"C:\project\readme.md"));
    }

    [Fact]
    public void IsClaimed_AnExactNameClaim_IsTrue()
    {
        // Arrange.
        var make = new EditorDefinition("make", "Makefiles", FileNames: ["Makefile"]);

        // Act and assert.
        Assert.True(Resolver(Plain, make).IsClaimed(@"C:\project\Makefile"));
    }

    [Fact]
    public void IsClaimed_AnAmbiguousExtension_IsStillTrue()
    {
        // Arrange.
        // The test is whether an editor claims the file, not whether opening will succeed:
        // two editors fighting over .md is a deployment fault the log already reports, and
        // the claim stays true (small-refinements Requirement 3.2).
        var one = new EditorDefinition("one", "One", Extensions: [".md"]);
        var two = new EditorDefinition("two", "Two", Extensions: [".md"]);

        // Act and assert.
        Assert.True(Resolver(Plain, one, two).IsClaimed(@"C:\project\readme.md"));
    }

    [Fact]
    public void IsClaimed_AFileOnlyTheFallbackAnswers_IsFalse()
    {
        // Arrange, act and assert: the fallback claims everything by construction, so it
        // never counts as a claim.
        Assert.False(Resolver(Plain, Markdown).IsClaimed(@"C:\project\notes.txt"));
    }
}
