using Xunit;
using Alpha = EtAlii.Adp.Diagram.Tests.Fixtures.Ordering.Alpha;
using Duplicate = EtAlii.Adp.Diagram.Tests.Fixtures.Duplicate;
using EmptyArray = EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.EmptyArray;
using FolderWithExtension = EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.FolderWithExtension;
using NotStatic = EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.NotStatic;
using NullEntry = EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.NullEntry;
using Several = EtAlii.Adp.Diagram.Tests.Fixtures.Several;
using Throws = EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.Throws;
using Valid = EtAlii.Adp.Diagram.Tests.Fixtures.Valid;
using WrongType = EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.WrongType;
using Zulu = EtAlii.Adp.Diagram.Tests.Fixtures.Ordering.Zulu;

namespace EtAlii.Adp.Diagram.Tests;

[Collection(LogCapture.Collection)]
public class DiagramDefinitionDiscoveryTests : IDisposable
{
    // Discovery logs through a static Serilog logger, so what it said is read from a capture
    // over the pipeline rather than from a logger handed to it.
    private readonly LogCapture _logger = LogCapture.Start();

    public void Dispose() => _logger.Dispose();

    private static FakeAssembly AssemblyWith(string name, params Type[] types) => new(name, types);

    // ---- construction and arguments -----------------------------------------------------

    [Fact]
    public void Discover_WithNullAssemblies_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentNullException>(() => DiagramDefinitionDiscovery.Discover(null!));
    }

    // ---- the happy path -----------------------------------------------------------------

    [Fact]
    public void Discover_FindsAStaticDiagramClassWithAPublicStaticDefinition()
    {
        // Arrange.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Valid.Diagram))]);

        // Act and assert, step by step.
        var definition = Assert.Single(result);
        Assert.Equal(new DiagramOrigin("fixture", "valid"), definition.Origin);
        Assert.Equal("Valid Fixture", definition.Title);
    }

    [Fact]
    public void Discover_LogsEachDiscoveredTypeWithOriginTitleAndAssembly()
    {
        // Act.
        DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Valid.Diagram))]);

        // Assert.
        Assert.Contains("Discovered diagram type fixture/valid: Valid Fixture (Fixture.A)", _logger.Informations);
    }

    [Fact]
    public void Discover_LogsASummaryWithCountAndAssembliesScanned()
    {
        // Arrange and act.
        DiagramDefinitionDiscovery.Discover([
            AssemblyWith("Fixture.A", typeof(Valid.Diagram)),
            AssemblyWith("Fixture.B", typeof(Zulu.Diagram)),
            AssemblyWith("Fixture.C"),
        ]);

        // Assert.
        Assert.Contains("2 diagram types discovered across 3 assemblies", _logger.Informations);
    }

    [Fact]
    public void Discover_IgnoresTypesThatAreNotNamedDiagram()
    {
        // Act.
        // Anything else in an assembly - this test class, for instance - is simply not a candidate.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(DiagramDefinitionDiscoveryTests), typeof(Valid.Diagram))]);

        // Assert.
        Assert.Single(result);
        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public void Discover_AgainstTheRealTestAssembly_FindsTheValidFixtureAmongTheOthers()
    {
        // Act.
        // The fixtures all live in this assembly, so a real scan sees them together: the valid
        // one and its duplicate collapse to one, the malformed ones are reported, the
        // non-static one is passed over. This is the closest thing to a real module scan.
        var result = DiagramDefinitionDiscovery.Discover([typeof(DiagramDefinitionDiscoveryTests).Assembly]);

        // Assert.
        Assert.Contains(result, d => d.Origin == new DiagramOrigin("fixture", "valid"));
        Assert.Contains(result, d => d.Origin == new DiagramOrigin("alpha", "z"));
        Assert.Contains(result, d => d.Origin == new DiagramOrigin("zulu", "a"));
        Assert.DoesNotContain(result, d => d.Origin == new DiagramOrigin("fixture", "not-static"));
        // The three types the Several fixture declares from one class - the shape that made the
        // property plural - and the survivor from the array with a null in it.
        Assert.Contains(result, d => d.Origin == new DiagramOrigin("several", "first"));
        Assert.Contains(result, d => d.Origin == new DiagramOrigin("several", "second"));
        Assert.Contains(result, d => d.Origin == new DiagramOrigin("several", "third"));
        Assert.Contains(result, d => d.Origin == new DiagramOrigin("fixture", "survivor"));
        Assert.Equal(7, result.Count);
    }

    // ---- ordering -----------------------------------------------------------------------

    [Fact]
    public void Discover_OrdersByVendorThenType_RegardlessOfInputOrder()
    {
        // Arrange.
        var forward = DiagramDefinitionDiscovery.Discover([AssemblyWith("A", typeof(Zulu.Diagram), typeof(Alpha.Diagram), typeof(Valid.Diagram))]);
        var backward = DiagramDefinitionDiscovery.Discover([AssemblyWith("A", typeof(Valid.Diagram), typeof(Alpha.Diagram), typeof(Zulu.Diagram))]);

        // Act and assert, step by step.
        var expected = new[] { "alpha/z", "fixture/valid", "zulu/a" };
        Assert.Equal(expected, forward.Select(d => d.Origin.Key));
        Assert.Equal(expected, backward.Select(d => d.Origin.Key));
    }

    // ---- malformed candidates -----------------------------------------------------------

    [Fact]
    public void Discover_SkipsADefinitionOfTheWrongType_AndSaysWhich()
    {
        // Act.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(WrongType.Diagram))]);

        // Assert.
        Assert.Empty(result);
        var warning = Assert.Single(_logger.Warnings, w => w.Contains("malformed", StringComparison.Ordinal));
        Assert.Contains(typeof(WrongType.Diagram).FullName!, warning, StringComparison.Ordinal);
        Assert.Contains("Fixture.A", warning, StringComparison.Ordinal);
        Assert.Contains("not a sequence of DiagramDefinition", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_SkipsADefinitionWhoseGetterThrows_AndReportsTheCause()
    {
        // Act.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Throws.Diagram))]);

        // Assert.
        Assert.Empty(result);
        var warning = Assert.Single(_logger.Warnings, w => w.Contains("malformed", StringComparison.Ordinal));
        // The getter's own message, not the TargetInvocationException wrapper's.
        Assert.Contains("fixture getter failure", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_PassesOverANonStaticClassNamedDiagram_Silently()
    {
        // Act.
        // Not malformed - not a candidate at all, so it earns no warning.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(NotStatic.Diagram))]);

        // Assert.
        Assert.Empty(result);
        Assert.DoesNotContain(_logger.Warnings, w => w.Contains("malformed", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_AMalformedCandidate_DoesNotStopTheValidOnesBeingFound()
    {
        // Act.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Throws.Diagram), typeof(Valid.Diagram), typeof(WrongType.Diagram))]);

        // Assert.
        Assert.Single(result);
    }

    // ---- several definitions from one class ----------------------------------------------

    [Fact]
    public void Discover_ReadsEveryDefinitionAClassDeclares_NotJustTheFirst()
    {
        // Act.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Several.Diagram))]);

        // Assert.
        Assert.Equal(
            ["several/first", "several/second", "several/third"],
            result.Select(definition => definition.Origin.Key));
    }

    [Fact]
    public void Discover_LogsEachDefinitionSeparately_EvenWhenOneClassDeclaresThemAll()
    {
        // Act.
        // Otherwise a module carrying seven types would announce itself once and leave the log
        // disagreeing with the count in the summary line underneath it.
        DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Several.Diagram))]);

        // Assert.
        Assert.Equal(3, _logger.Informations.Count(entry => entry.Contains("Discovered diagram type", StringComparison.Ordinal)));
    }

    [Fact]
    public void Discover_MixesDefinitionsFromOneClassAndManyIntoOneOrderedResult()
    {
        // Act.
        // Ordering is over definitions, not over the classes they came from - a module that
        // declares three must not arrive as a block wherever its class happened to be scanned.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Several.Diagram), typeof(Alpha.Diagram), typeof(Zulu.Diagram))]);

        // Assert.
        Assert.Equal(
            ["alpha/z", "several/first", "several/second", "several/third", "zulu/a"],
            result.Select(definition => definition.Origin.Key));
    }

    [Fact]
    public void Discover_ADefinitionsArrayDeclaringNothing_IsReportedRatherThanPassedOver()
    {
        // Act.
        // A module that ships no types is almost certainly a mistake, and it is a mistake the
        // singular property could not express - so it is new here and worth saying out loud.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(EmptyArray.Diagram))]);

        // Assert.
        Assert.Empty(result);
        var warning = Assert.Single(_logger.Warnings, w => w.Contains("malformed", StringComparison.Ordinal));
        Assert.Contains("declares nothing", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_ANullAmongTheDefinitions_CostsThatEntryAndNotTheGoodOnesBesideIt()
    {
        // Act.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(NullEntry.Diagram))]);

        // Assert.
        var definition = Assert.Single(result);
        Assert.Equal("fixture/survivor", definition.Origin.Key);
        var warning = Assert.Single(_logger.Warnings, w => w.Contains("malformed", StringComparison.Ordinal));
        Assert.Contains("1 null entry", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_AFolderSubjectDeclaringAnExtension_CostsThatEntryAndNotTheGoodOnesBesideIt()
    {
        // Act.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(FolderWithExtension.Diagram))]);

        // Assert.
        // A folder has no sibling body, so the two claims cannot both be true. Dropped rather
        // than half-believed, and the good definition beside it is unaffected.
        var definition = Assert.Single(result);
        Assert.Equal("fixture/survivor", definition.Origin.Key);
        var warning = Assert.Single(_logger.Warnings, w => w.Contains("malformed", StringComparison.Ordinal));
        Assert.Contains("fixture/contradiction", warning, StringComparison.Ordinal);
        Assert.Contains("cannot both be true", warning, StringComparison.Ordinal);
    }

    // ---- origin collisions --------------------------------------------------------------

    [Fact]
    public void Discover_OnAnOriginCollision_KeepsTheOrdinalFirstAssemblyAndWarnsNamingBoth()
    {
        // Arrange.
        // Zeta carries the Valid fixture, Alpha the duplicate with the same origin. Alpha sorts
        // first, so its definition wins - even though Zeta was handed in first.
        var result = DiagramDefinitionDiscovery.Discover([
            AssemblyWith("Zeta", typeof(Valid.Diagram)),
            AssemblyWith("Alpha", typeof(Duplicate.Diagram)),
        ]);

        // Act and assert, step by step.
        var definition = Assert.Single(result);
        Assert.Equal("Duplicate Of Valid", definition.Title);

        var warning = Assert.Single(_logger.Warnings);
        Assert.Contains("fixture/valid", warning, StringComparison.Ordinal);
        Assert.Contains("Zeta", warning, StringComparison.Ordinal);
        Assert.Contains("Alpha", warning, StringComparison.Ordinal);
        Assert.EndsWith("keeping the one from Alpha", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_OnAnOriginCollision_TheOutcomeDoesNotDependOnInputOrder()
    {
        // Arrange and act.
        var alphaFirst = DiagramDefinitionDiscovery.Discover([
            AssemblyWith("Alpha", typeof(Duplicate.Diagram)),
            AssemblyWith("Zeta", typeof(Valid.Diagram)),
        ]);
        var zetaFirst = DiagramDefinitionDiscovery.Discover([
            AssemblyWith("Zeta", typeof(Valid.Diagram)),
            AssemblyWith("Alpha", typeof(Duplicate.Diagram)),
        ]);

        // Assert.
        Assert.Equal("Duplicate Of Valid", Assert.Single(alphaFirst).Title);
        Assert.Equal("Duplicate Of Valid", Assert.Single(zetaFirst).Title);
    }

    // ---- assemblies that cannot be read -------------------------------------------------

    [Fact]
    public void Discover_WhenAnAssemblyLoadsOnlyPartially_ScansTheTypesThatDidLoad()
    {
        // Arrange.
        var partial = FakeAssembly.PartiallyLoadable("Fixture.Partial", [typeof(Valid.Diagram)], new FileNotFoundException("dep.dll"));

        // Act.
        var result = DiagramDefinitionDiscovery.Discover([partial]);

        // Assert.
        Assert.Single(result);
        Assert.Contains(_logger.Warnings, w => w.Contains("loaded only partially", StringComparison.Ordinal) && w.Contains("Fixture.Partial", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_WhenAnAssemblyCannotBeEnumerated_SkipsItAndContinues()
    {
        // Arrange.
        var broken = FakeAssembly.Broken("Fixture.Broken", new BadImageFormatException("corrupt"));

        // Act.
        var result = DiagramDefinitionDiscovery.Discover([broken, AssemblyWith("Fixture.A", typeof(Valid.Diagram))]);

        // Assert.
        Assert.Single(result);
        var warning = Assert.Single(_logger.Warnings);
        Assert.Contains("Fixture.Broken", warning, StringComparison.Ordinal);
        Assert.Contains("could not be enumerated", warning, StringComparison.Ordinal);
        // The broken assembly still counts as scanned: it was handed in and looked at.
        Assert.Contains("1 diagram types discovered across 2 assemblies", _logger.Informations);
    }

    // ---- nothing found ------------------------------------------------------------------

    [Fact]
    public void Discover_WithNoAssemblies_ReturnsEmptyAndWarns()
    {
        // Act.
        var result = DiagramDefinitionDiscovery.Discover([]);

        // Assert.
        Assert.Empty(result);
        Assert.Contains("0 diagram types discovered across 0 assemblies", _logger.Informations);
        Assert.Contains(_logger.Warnings, w => w.StartsWith("No diagram types were discovered", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_WithAssembliesButNoDiagramClasses_ReturnsEmptyAndWarns()
    {
        // Act.
        var result = DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.Empty", typeof(DiagramDefinitionDiscoveryTests))]);

        // Assert.
        Assert.Empty(result);
        Assert.Contains(_logger.Warnings, w => w.StartsWith("No diagram types were discovered", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_WhenSomethingIsFound_DoesNotRaiseTheNothingFoundWarning()
    {
        // Act.
        DiagramDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Valid.Diagram))]);

        // Assert.
        Assert.DoesNotContain(_logger.Warnings, w => w.StartsWith("No diagram types were discovered", StringComparison.Ordinal));
    }
}
