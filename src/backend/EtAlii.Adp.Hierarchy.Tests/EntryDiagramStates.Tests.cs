using EtAlii.Adp.Common;
using EtAlii.Adp.Editor;
using EtAlii.Adp.TestSupport;
using Xunit;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// The three-valued explorer state, one fact per rule (small-refinements Requirements 3.1,
/// 3.2, 3.3, 3.5). The decision is pure over the names the folder scan already holds; the
/// routing knowledge is the router's and resolver's own queries over stub catalogs, so what
/// these tests exercise is the same path the model will wire.
/// </summary>
public class EntryDiagramStatesTests
{
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");
    private static readonly DiagramDefinition Pipeline = new(new DiagramOrigin("azure-devops", "pipeline"), "Azure DevOps pipeline", Extension: ".yml", SharedExtension: true);
    private static readonly EditorDefinition Plain = new("plain", "Plain Text", IsFallback: true);
    private static readonly EditorDefinition Markdown = new("markdown", "Markdown", Extensions: [".md"]);

    private sealed class StubEditorCatalog(params EditorDefinition[] definitions) : IEditorDefinitionCatalog
    {
        public IReadOnlyList<EditorDefinition> All { get; } = definitions;
    }

    /// <summary>The decision wired the way the model will wire it, over the given names.</summary>
    private static Hierarchy.EntryDiagramState Decide(
        string name,
        bool isFolder,
        IReadOnlyCollection<string> names,
        Func<string, string?>? resolvedBodyNameOf = null,
        Func<string, bool>? declaresFolderSubject = null)
    {
        var router = new DiagramFileRouter(new TestDiagramDefinitionCatalog(Mindmap, Pipeline));
        var resolver = new EditorResolver(new StubEditorCatalog(Plain, Markdown));
        return EntryDiagramStates.Decide(
            name,
            isFolder,
            names,
            resolvedBodyNameOf ?? (_ => null),
            router.ClaimsExtensionOf,
            resolver.IsClaimed,
            declaresFolderSubject ?? (_ => false));
    }

    [Fact]
    public void ARegistrationFileItself_IsRegistered()
    {
        // Arrange, act and assert.
        Assert.Equal(Hierarchy.EntryDiagramState.Registered, Decide("ideas.adp", isFolder: false, ["ideas.adp", "ideas.mm"]));
    }

    [Fact]
    public void AFileWithASameNamedRegistrationBesideIt_IsRegistered()
    {
        // Arrange, act and assert.
        Assert.Equal(Hierarchy.EntryDiagramState.Registered, Decide("ideas.mm", isFolder: false, ["ideas.adp", "ideas.mm"]));
    }

    [Fact]
    public void AFileASiblingRegistrationResolvesAsItsBody_IsRegistered()
    {
        // Arrange.
        // The courier.dsl-beside-courier.adp shape from Requirement 3.1, generalised: the
        // registration's body: resolution - not its name - is what lands on this file.
        static string? BodyOf(string registration) =>
            registration == "design.adp" ? "model.mm" : null;

        // Act and assert.
        Assert.Equal(
            Hierarchy.EntryDiagramState.Registered,
            Decide("model.mm", isFolder: false, ["design.adp", "model.mm"], BodyOf));
    }

    [Fact]
    public void AFileWhoseExtensionADiagramTypeClaims_IsPotential_SharedIncluded()
    {
        // Arrange, act and assert.
        // The shared .yml is the branch that distinguishes this from Route: registrable
        // counts as potential (Requirement 3.2).
        Assert.Equal(Hierarchy.EntryDiagramState.Potential, Decide("build.yml", isFolder: false, ["build.yml"]));
    }

    [Fact]
    public void AFileANonFallbackEditorClaims_IsPotential()
    {
        // Arrange, act and assert.
        Assert.Equal(Hierarchy.EntryDiagramState.Potential, Decide("readme.md", isFolder: false, ["readme.md"]));
    }

    [Fact]
    public void AFileNothingClaims_IsNeutral()
    {
        // Arrange, act and assert: the fallback answers it, and the fallback never counts
        // (Requirement 3.3) - zero, so an older peer's silence reads the same.
        Assert.Equal(Hierarchy.EntryDiagramState.Unspecified, Decide("notes.txt", isFolder: false, ["notes.txt"]));
    }

    [Theory]
    [InlineData("structure.adp")]
    [InlineData(".adp")]
    public void AFolderContainingAFolderSubjectRegistration_IsRegistered(string registration)
    {
        // Arrange.
        // The registration lives INSIDE the folder it registers (infrastructure/structure.adp),
        // per the design's correction of Requirement 3.1's parenthetical.
        //
        // Both shapes resolve, and keeping the named one is the point rather than tidiness:
        // creation is constrained by folder-add-registration and reading is not. Add writes a
        // bare ".adp" from now on, but a user's existing infrastructure/structure.adp has to
        // keep opening, and so does anything a previous version of ADP wrote.
        //
        // The predicate is this test's stand-in for routing. Production reads the MIME line
        // inside the file and the name never enters the decision - which is exactly why the
        // name may vary without the read path caring.
        bool DeclaresFolderSubject(string candidate) => candidate == registration;

        // Act and assert.
        Assert.Equal(
            Hierarchy.EntryDiagramState.Registered,
            Decide("infrastructure", isFolder: true, [registration, "site.yml"], declaresFolderSubject: DeclaresFolderSubject));
    }

    [Fact]
    public void AFolderWithoutOne_IsNeutral_NeverPotential()
    {
        // Arrange, act and assert: contents a diagram type would claim do not make the folder
        // potential - greening every directory would say nothing.
        Assert.Equal(Hierarchy.EntryDiagramState.Unspecified, Decide("docs", isFolder: true, ["ideas.mm", "readme.md"]));
    }
}
