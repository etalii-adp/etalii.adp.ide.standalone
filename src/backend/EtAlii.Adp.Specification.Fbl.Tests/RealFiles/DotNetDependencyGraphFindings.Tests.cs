using System.Text;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.RealFiles;

/// <summary>
/// The two findings of dotnet-dependency-graph-disl-fbl whose FBL side is the library's registration
/// support, which is internal to it: B11's registration without a <c>body:</c> header and B14's order
/// of layout entries. Each reads its fixture of the .NET dependency graph's test project, as the rest
/// of this folder reads the real files under <c>src/</c>, and sets what core does beside what FBL, as
/// this library implements it, does. The module's other findings are in its own
/// <c>Findings.Tests.cs</c>.
/// </summary>
/// <remarks>
/// Each test computes the difference as one sentence, null when FBL's way gives what core gives, and
/// holds it to the sentence pinned beside it. Both were first run asserting the difference was null,
/// and failed with the sentence they now pin; a pinned sentence that changes, or a finding that no
/// longer occurs, fails here.
/// </remarks>
public sealed class DotNetDependencyGraphFindingsTests
{
    /// <summary>The primary extension of the .NET dependency graph today, from which core derives a header-less registration's body.</summary>
    private const string PrimaryExtension = ".sln";

    /// <summary>The binding of the requirements' appendix, as far as finding a body depends on it: its claims.</summary>
    private const string DraftBinding = """
        {
          "fbl": "0.2",
          "bindings": {
            "solution": {
              "claims": { "extensions": [".slnx", ".sln"], "origins": ["dotnet/dependency-graph"], "shared": true, "registrationOnly": true },
              "body": { "kind": "file" },
              "reader": { "plugin": "net.etalii.adp.dotnet.solution", "version": "^0.1.0" },
              "readOnly": true
            }
          }
        }
        """;

    private static string Fixtures { get; } = RepositoryPaths.Under(
        "src", "diagrams", "dotnet-dependency-graph", "backend", "EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests", "Fixtures");

    [Fact]
    public void B11_ARegistrationWithoutABodyHeaderBesideBothSerializations_OpensWhatTheOrderOfExtensionsSays()
    {
        // Arrange.
        var registration = Path.Combine(Fixtures, "headerless", "Both.adp");

        // Act: core derives the sibling from the primary extension; FBL 8.2 takes the first of
        // claims.extensions that exists, in the order the draft binding gives them.
        var core = DiagramFilePair.SiblingPathFor(registration, PrimaryExtension);
        var located = BodyLocator.Locate(registration, RegistrationDocument.Read(File.ReadAllBytes(registration)), Binding(), Fixtures);

        var observed = string.Equals(core, located.Path, StringComparison.Ordinal)
            ? null
            : $"core opens {Path.GetFileName(core)}; FBL 8.2 under the draft's claims.extensions [.slnx, .sln] opens {Path.GetFileName(located.Path)}";

        // Assert.
        Shows("B11", observed, "core opens Both.sln; FBL 8.2 under the draft's claims.extensions [.slnx, .sln] opens Both.slnx");
    }

    [Fact]
    public void B14_APlacedId_GoesWhereEachWritersOrderPutsIt()
    {
        // Arrange: the registration holds an id with U+FF21; the id placed holds U+1D538, which
        // UTF-16 orders before it and UTF-8 after it.
        var bytes = File.ReadAllBytes(Path.Combine(Fixtures, "registration-order", "Order.adp"));
        const string placed = "project:\U0001D538/\U0001D538.csproj";

        // Act: core rewrites the block with the new position; the library splices one entry in.
        var registration = AdpRegistration.Parse(Encoding.UTF8.GetString(bytes));
        var positions = new Dictionary<string, RegistrationPosition>(registration.Layout, StringComparer.Ordinal) { [placed] = new(30, 40) };
        var core = Encoding.UTF8.GetBytes(registration.WithLayout(positions));
        var library = OpenRegistration.Open(bytes);
        Assert.IsType<PlanResult.Planned>(library.Change(new ModelChange.Place(placed, 30, 40)));

        var observed = core.AsSpan().SequenceEqual(library.Bytes)
            ? null
            : $"core writes the layout as [{LayoutKeys(core)}]; the library writes it as [{LayoutKeys(library.Bytes)}]";

        // Assert.
        Shows("B14", observed,
            "core writes the layout as [project:\U0001D538/\U0001D538.csproj, project:\uFF21/\uFF21.csproj]; the library writes it as [project:\uFF21/\uFF21.csproj, project:\U0001D538/\U0001D538.csproj]");
    }

    /// <summary>
    /// Holds a finding's difference to the one pinned: <paramref name="observed"/> is null when FBL's
    /// way gives what core gives.
    /// </summary>
    private static void Shows(string finding, string? observed, string pinned)
    {
        Assert.True(observed is not null, $"{finding} no longer occurs on its fixture; strike or update its pinned sentence.");
        Assert.True(observed == pinned, $"{finding} has changed.\nPinned:   {pinned}\nObserved: {observed}");
    }

    /// <summary>The keys of a registration's layout block, in the order they are written.</summary>
    private static string LayoutKeys(byte[] registration) =>
        string.Join(", ", AdpRegistration.Parse(Encoding.UTF8.GetString(registration)).Layout.Keys);

    private static FblBinding Binding()
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(DraftBinding), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return document!.Bindings["solution"];
    }
}
