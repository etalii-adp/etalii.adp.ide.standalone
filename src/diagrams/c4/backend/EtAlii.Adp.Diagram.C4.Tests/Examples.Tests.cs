using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Common;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The example projects under <c>src/diagrams/c4/examples/</c>, checked against the
/// implementation they are examples of.
/// </summary>
/// <remarks>
/// An example that no longer opens is worse than no example: it is a confident, wrong statement
/// about what the tool does. These tests are cheap and they are what stops that - every model
/// parses, every registration resolves to a view that exists, and nothing in either project
/// breaks a C4 rule.
/// <para>
/// Both models are also certified by the real Structurizr CLI - `validate` accepts them and
/// `inspect` reports nothing - which is asserted for the fixture corpus in
/// <see cref="C4InteropTests"/> and re-checked by hand whenever these files change.
/// </para>
/// </remarks>
public class ExamplesTests
{
    /// <summary>
    /// The examples folder, found by walking up from the test binary rather than by counting
    /// `..` segments - the count changes with the build layout, the folder name does not.
    /// </summary>
    private static string ExamplesRoot { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "examples");
            if (Directory.Exists(candidate) && Directory.Exists(IoPath.Combine(directory.FullName, "backend")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("The c4 module's examples folder could not be found from " + AppContext.BaseDirectory);
    }

    public static TheoryData<string> EveryProject() => ["reference", "industrial-plant"];

    private static string[] ModelsOf(string project) =>
        Directory.GetFiles(IoPath.Combine(ExamplesRoot, project), "*.dsl", SearchOption.AllDirectories);

    private static C4Workspace Read(string dslPath) => C4Parser.Parse(C4Document.Parse(File.ReadAllText(dslPath)));

    [Theory]
    [MemberData(nameof(EveryProject))]
    public void EveryProject_HasExactlyOneModelDocument(string project)
    {
        // Act and assert.
        // One document per project is the arrangement both examples are demonstrating: several
        // diagrams over one model, which is what makes a rename in one view a rename in all.
        var models = ModelsOf(project);

        Assert.Single(models);
    }

    [Theory]
    [MemberData(nameof(EveryProject))]
    public void EveryModel_Parses_AndRoundTripsByteForByte(string project)
    {
        // Arrange.
        var path = ModelsOf(project).Single();
        var text = File.ReadAllText(path);

        // Act.
        var document = C4Document.Parse(text);

        // Assert.
        Assert.Equal(text, document.ToText());
        Assert.NotEmpty(C4Parser.Parse(document).Elements);
    }

    [Theory]
    [MemberData(nameof(EveryProject))]
    public void EveryRegistration_NamesAViewThatExists(string project)
    {
        // Arrange.
        // The registration files are the half a reader clicks on. A `view:` header naming a view
        // the model does not have opens an empty canvas, which is exactly the sort of rot an
        // example acquires when the model beside it is edited.
        var root = IoPath.Combine(ExamplesRoot, project);
        var workspace = Read(ModelsOf(project).Single());

        // Act and assert, one registration at a time.
        var registrations = Directory.GetFiles(root, "*.adp", SearchOption.AllDirectories);
        Assert.NotEmpty(registrations);

        foreach (var registration in registrations)
        {
            var lines = File.ReadAllLines(registration);
            var name = IoPath.GetFileName(registration);

            Assert.StartsWith("c4/", lines[0], StringComparison.Ordinal);

            var view = lines.FirstOrDefault(line => line.StartsWith("view:", StringComparison.Ordinal));
            if (view is null)
            {
                // Legal, and only for the one type that keeps no document: c4/code has no
                // extension, so its registration is the whole diagram.
                Assert.Equal("c4/code", lines[0]);
                continue;
            }

            var key = view["view:".Length..].Trim();
            Assert.True(workspace.FindView(key) is not null, $"{name} names view '{key}', which {project} does not have.");
        }
    }

    [Theory]
    [MemberData(nameof(EveryProject))]
    public void EveryRegistration_ResolvesThroughTheSameCodeTheBackendUses(string project)
    {
        // Arrange.
        // Through DiagramFilePair rather than by reading the headers here: a test that parses
        // the files its own way can agree with itself while disagreeing with the backend, and
        // then the example still does not open.
        var root = IoPath.Combine(ExamplesRoot, project);
        var catalog = new C4ExamplesStubCatalog();

        // Act and assert, registration by registration.
        foreach (var registration in Directory.GetFiles(root, "*.adp", SearchOption.AllDirectories))
        {
            var name = IoPath.GetFileName(registration);
            var body = DiagramFilePair.BodyOf(registration, catalog, root);

            if (File.ReadLines(registration).First() == "c4/code")
            {
                // The one type that keeps no document: its .adp is the whole diagram.
                Assert.Null(body);
                continue;
            }

            // A readonly record struct, so the nullable is Nullable<T> and needs unwrapping.
            Assert.True(body.HasValue, $"{name} resolves to no body at all.");
            Assert.True(File.Exists(body.Value.Path), $"{name} resolves to a body that does not exist: {body.Value.Path}");
        }
    }

    [Theory]
    [MemberData(nameof(EveryProject))]
    public void ExactlyOneRegistrationPerProject_OwnsTheModelDocument(string project)
    {
        // Arrange.
        // Ownership decides what a delete or a rename carries off with it. More than one owner
        // is impossible by construction; none would mean the model is nobody's to remove.
        var root = IoPath.Combine(ExamplesRoot, project);
        var catalog = new C4ExamplesStubCatalog();

        // Act.
        var owners = Directory.GetFiles(root, "*.adp", SearchOption.AllDirectories)
            .Where(registration => DiagramFilePair.BodyOf(registration, catalog, root) is { IsOwned: true })
            .ToArray();

        // Assert.
        var owner = Assert.Single(owners);
        Assert.Equal(
            IoPath.GetFileNameWithoutExtension(ModelsOf(project).Single()),
            IoPath.GetFileNameWithoutExtension(owner));
    }

    [Theory]
    [MemberData(nameof(EveryProject))]
    public void EveryModel_IsCleanByC4sRules(string project)
    {
        // Act.
        // Both examples are meant to be exemplary rather than merely parseable, so anything
        // reported here is a defect in the example - not a rule to be narrowed.
        var problems = C4RuleSet.Validate(Read(ModelsOf(project).Single()))
            .Select(problem => $"{problem.RuleId}: {problem.Message}")
            .ToArray();

        // Assert.
        Assert.Empty(problems);
    }

    [Theory]
    [MemberData(nameof(EveryProject))]
    public void EveryViewInEveryModel_LaysOutWithoutOverlaps(string project)
    {
        // Arrange.
        var workspace = Read(ModelsOf(project).Single());

        // Act and assert, view by view.
        foreach (var view in workspace.Views)
        {
            var boxes = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default).Boxes.ToArray();
            for (var i = 0; i < boxes.Length; i++)
            {
                for (var j = i + 1; j < boxes.Length; j++)
                {
                    Assert.False(
                        boxes[i].Value.Overlaps(boxes[j].Value),
                        $"{project}/{view.Key}: '{boxes[i].Key}' overlaps '{boxes[j].Key}'");
                }
            }
        }
    }

    [Fact]
    public void TheReferenceProject_ShowsEverySupportedViewKind()
    {
        // Act.
        // The point of the reference project: if a view kind is missing from it, the example has
        // stopped being a statement of what ADP supports.
        var kinds = Read(ModelsOf("reference").Single()).Views.Select(view => view.Kind).ToArray();

        // Assert.
        Assert.Equal(
            Enum.GetValues<C4ViewKind>().Order(),
            kinds.Distinct().Order());
    }

    [Fact]
    public void TheReferenceProject_RegistersEverySupportedDiagramType()
    {
        // Act.
        // Including c4/code, whose registration exists precisely to show what a type with no
        // canvas looks like in the explorer.
        var declared = Directory
            .GetFiles(IoPath.Combine(ExamplesRoot, "reference"), "*.adp", SearchOption.AllDirectories)
            .Select(path => File.ReadLines(path).First())
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert.
        Assert.Equal(
            Diagram.Definitions.Select(definition => definition.Origin.Key).Order(StringComparer.Ordinal),
            declared);
    }

    [Theory]
    [MemberData(nameof(EveryProject))]
    public async Task ValidatingAProject_Succeeds_AndReportsNothing(string project)
    {
        // Arrange.
        // The whole project through the real ProjectValidator, which is what the Validate action
        // on the explorer root runs. This is the check that would have caught the crash these
        // examples first hit: every registration but the owning one reaches its model through a
        // `body:` header, and the validator used to route without a project root to resolve one
        // against - so it walked into `Path.GetFullPath("")` and took the whole run down.
        var validator = new ProjectValidator(
            new DiagramFileRouter(new C4ExamplesStubCatalog()),
            new DiagramValidators(Diagram.Definitions.Select(definition => new C4Validator(definition.Origin))));

        // Act.
        var outcome = await validator.ValidateAsync(
            new ProjectValidationScope(IoPath.Combine(ExamplesRoot, project)),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(outcome.Problems.Select(problem => $"{problem.RelativePath}: {problem.Problem.Message}"));
        Assert.Equal(0, outcome.Skipped);

        // One judgement per document, however many registrations point at it - that is the whole
        // economy of the arrangement. Plus one for every registration of a type that keeps no
        // document: c4/code declares no extension, so its .adp is the whole diagram and is judged
        // in its own right. The reference project has one of those; the industrial one has none.
        var bodyless = Directory
            .GetFiles(IoPath.Combine(ExamplesRoot, project), "*.adp", SearchOption.AllDirectories)
            .Count(path => Diagram.Definitions
                .Any(definition => definition.Origin.Key == File.ReadLines(path).First() && !definition.HasDocumentSibling));

        Assert.Equal(1 + bodyless, outcome.FilesConsidered);
    }
}
