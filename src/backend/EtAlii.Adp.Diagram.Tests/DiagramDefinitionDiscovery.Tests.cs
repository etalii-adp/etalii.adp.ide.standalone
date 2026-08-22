using System.Reflection;
using Xunit;
using Valid = EtAlii.Adp.Diagram.Tests.Fixtures.Valid;
using Duplicate = EtAlii.Adp.Diagram.Tests.Fixtures.Duplicate;
using WrongType = EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.WrongType;
using NotStatic = EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.NotStatic;
using Throws = EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.Throws;
using Zulu = EtAlii.Adp.Diagram.Tests.Fixtures.Ordering.Zulu;
using Alpha = EtAlii.Adp.Diagram.Tests.Fixtures.Ordering.Alpha;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramDefinitionDiscoveryTests
{
    private readonly ListLogger<DiagramDefinitionDiscovery> _logger = new();
    private readonly DiagramDefinitionDiscovery _discovery;

    public DiagramDefinitionDiscoveryTests()
    {
        _discovery = new DiagramDefinitionDiscovery(_logger);
    }

    private static FakeAssembly AssemblyWith(string name, params Type[] types) => new(name, types);

    // ---- construction and arguments -----------------------------------------------------

    [Fact]
    public void Constructor_WithNullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DiagramDefinitionDiscovery(null!));
    }

    [Fact]
    public void Discover_WithNullAssemblies_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _discovery.Discover(null!));
    }

    // ---- the happy path -----------------------------------------------------------------

    [Fact]
    public void Discover_FindsAStaticDiagramClassWithAPublicStaticDefinition()
    {
        var result = _discovery.Discover([AssemblyWith("Fixture.A", typeof(Valid.Diagram))]);

        var definition = Assert.Single(result);
        Assert.Equal(new DiagramOrigin("fixture", "valid"), definition.Origin);
        Assert.Equal("Valid Fixture", definition.Title);
    }

    [Fact]
    public void Discover_LogsEachDiscoveredTypeWithOriginTitleAndAssembly()
    {
        _discovery.Discover([AssemblyWith("Fixture.A", typeof(Valid.Diagram))]);

        Assert.Contains("Discovered diagram type fixture/valid: Valid Fixture (Fixture.A)", _logger.Informations);
    }

    [Fact]
    public void Discover_LogsASummaryWithCountAndAssembliesScanned()
    {
        _discovery.Discover([
            AssemblyWith("Fixture.A", typeof(Valid.Diagram)),
            AssemblyWith("Fixture.B", typeof(Zulu.Diagram)),
            AssemblyWith("Fixture.C"),
        ]);

        Assert.Contains("2 diagram types discovered across 3 assemblies", _logger.Informations);
    }

    [Fact]
    public void Discover_IgnoresTypesThatAreNotNamedDiagram()
    {
        // Anything else in an assembly - this test class, for instance - is simply not a candidate.
        var result = _discovery.Discover([AssemblyWith("Fixture.A", typeof(DiagramDefinitionDiscoveryTests), typeof(Valid.Diagram))]);

        Assert.Single(result);
        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public void Discover_AgainstTheRealTestAssembly_FindsTheValidFixtureAmongTheOthers()
    {
        // The fixtures all live in this assembly, so a real scan sees them together: the valid
        // one and its duplicate collapse to one, the malformed ones are reported, the
        // non-static one is passed over. This is the closest thing to a real module scan.
        var result = _discovery.Discover([typeof(DiagramDefinitionDiscoveryTests).Assembly]);

        Assert.Contains(result, d => d.Origin == new DiagramOrigin("fixture", "valid"));
        Assert.Contains(result, d => d.Origin == new DiagramOrigin("alpha", "z"));
        Assert.Contains(result, d => d.Origin == new DiagramOrigin("zulu", "a"));
        Assert.DoesNotContain(result, d => d.Origin == new DiagramOrigin("fixture", "not-static"));
        Assert.Equal(3, result.Count);
    }

    // ---- ordering -----------------------------------------------------------------------

    [Fact]
    public void Discover_OrdersByVendorThenType_RegardlessOfInputOrder()
    {
        var forward = _discovery.Discover([AssemblyWith("A", typeof(Zulu.Diagram), typeof(Alpha.Diagram), typeof(Valid.Diagram))]);
        var backward = _discovery.Discover([AssemblyWith("A", typeof(Valid.Diagram), typeof(Alpha.Diagram), typeof(Zulu.Diagram))]);

        var expected = new[] { "alpha/z", "fixture/valid", "zulu/a" };
        Assert.Equal(expected, forward.Select(d => d.Origin.Key));
        Assert.Equal(expected, backward.Select(d => d.Origin.Key));
    }

    // ---- malformed candidates -----------------------------------------------------------

    [Fact]
    public void Discover_SkipsADefinitionOfTheWrongType_AndSaysWhich()
    {
        var result = _discovery.Discover([AssemblyWith("Fixture.A", typeof(WrongType.Diagram))]);

        Assert.Empty(result);
        var warning = Assert.Single(_logger.Warnings, w => w.Contains("malformed", StringComparison.Ordinal));
        Assert.Contains(typeof(WrongType.Diagram).FullName!, warning, StringComparison.Ordinal);
        Assert.Contains("Fixture.A", warning, StringComparison.Ordinal);
        Assert.Contains("not a DiagramDefinition", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_SkipsADefinitionWhoseGetterThrows_AndReportsTheCause()
    {
        var result = _discovery.Discover([AssemblyWith("Fixture.A", typeof(Throws.Diagram))]);

        Assert.Empty(result);
        var warning = Assert.Single(_logger.Warnings, w => w.Contains("malformed", StringComparison.Ordinal));
        // The getter's own message, not the TargetInvocationException wrapper's.
        Assert.Contains("fixture getter failure", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_PassesOverANonStaticClassNamedDiagram_Silently()
    {
        // Not malformed - not a candidate at all, so it earns no warning.
        var result = _discovery.Discover([AssemblyWith("Fixture.A", typeof(NotStatic.Diagram))]);

        Assert.Empty(result);
        Assert.DoesNotContain(_logger.Warnings, w => w.Contains("malformed", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_AMalformedCandidate_DoesNotStopTheValidOnesBeingFound()
    {
        var result = _discovery.Discover([AssemblyWith("Fixture.A", typeof(Throws.Diagram), typeof(Valid.Diagram), typeof(WrongType.Diagram))]);

        Assert.Single(result);
    }

    // ---- origin collisions --------------------------------------------------------------

    [Fact]
    public void Discover_OnAnOriginCollision_KeepsTheOrdinalFirstAssemblyAndWarnsNamingBoth()
    {
        // Zeta carries the Valid fixture, Alpha the duplicate with the same origin. Alpha sorts
        // first, so its definition wins - even though Zeta was handed in first.
        var result = _discovery.Discover([
            AssemblyWith("Zeta", typeof(Valid.Diagram)),
            AssemblyWith("Alpha", typeof(Duplicate.Diagram)),
        ]);

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
        var alphaFirst = _discovery.Discover([
            AssemblyWith("Alpha", typeof(Duplicate.Diagram)),
            AssemblyWith("Zeta", typeof(Valid.Diagram)),
        ]);
        var zetaFirst = _discovery.Discover([
            AssemblyWith("Zeta", typeof(Valid.Diagram)),
            AssemblyWith("Alpha", typeof(Duplicate.Diagram)),
        ]);

        Assert.Equal("Duplicate Of Valid", Assert.Single(alphaFirst).Title);
        Assert.Equal("Duplicate Of Valid", Assert.Single(zetaFirst).Title);
    }

    // ---- assemblies that cannot be read -------------------------------------------------

    [Fact]
    public void Discover_WhenAnAssemblyLoadsOnlyPartially_ScansTheTypesThatDidLoad()
    {
        var partial = FakeAssembly.PartiallyLoadable("Fixture.Partial", [typeof(Valid.Diagram)], new FileNotFoundException("dep.dll"));

        var result = _discovery.Discover([partial]);

        Assert.Single(result);
        Assert.Contains(_logger.Warnings, w => w.Contains("loaded only partially", StringComparison.Ordinal) && w.Contains("Fixture.Partial", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_WhenAnAssemblyCannotBeEnumerated_SkipsItAndContinues()
    {
        var broken = FakeAssembly.Broken("Fixture.Broken", new BadImageFormatException("corrupt"));

        var result = _discovery.Discover([broken, AssemblyWith("Fixture.A", typeof(Valid.Diagram))]);

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
        var result = _discovery.Discover([]);

        Assert.Empty(result);
        Assert.Contains("0 diagram types discovered across 0 assemblies", _logger.Informations);
        Assert.Contains(_logger.Warnings, w => w.StartsWith("No diagram types were discovered", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_WithAssembliesButNoDiagramClasses_ReturnsEmptyAndWarns()
    {
        var result = _discovery.Discover([AssemblyWith("Fixture.Empty", typeof(DiagramDefinitionDiscoveryTests))]);

        Assert.Empty(result);
        Assert.Contains(_logger.Warnings, w => w.StartsWith("No diagram types were discovered", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_WhenSomethingIsFound_DoesNotRaiseTheNothingFoundWarning()
    {
        _discovery.Discover([AssemblyWith("Fixture.A", typeof(Valid.Diagram))]);

        Assert.DoesNotContain(_logger.Warnings, w => w.StartsWith("No diagram types were discovered", StringComparison.Ordinal));
    }
}
