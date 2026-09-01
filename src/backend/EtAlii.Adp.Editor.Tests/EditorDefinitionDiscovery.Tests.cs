using Xunit;
using Alpha = EtAlii.Adp.Editor.Tests.Fixtures.Ordering.Alpha;
using Duplicate = EtAlii.Adp.Editor.Tests.Fixtures.Duplicate;
using EmptyArray = EtAlii.Adp.Editor.Tests.Fixtures.Malformed.EmptyArray;
using NoProperty = EtAlii.Adp.Editor.Tests.Fixtures.Malformed.NoProperty;
using NotStatic = EtAlii.Adp.Editor.Tests.Fixtures.Malformed.NotStatic;
using NullEntry = EtAlii.Adp.Editor.Tests.Fixtures.Malformed.NullEntry;
using Several = EtAlii.Adp.Editor.Tests.Fixtures.Several;
using Throws = EtAlii.Adp.Editor.Tests.Fixtures.Malformed.Throws;
using Valid = EtAlii.Adp.Editor.Tests.Fixtures.Valid;
using WrongType = EtAlii.Adp.Editor.Tests.Fixtures.Malformed.WrongType;
using Zulu = EtAlii.Adp.Editor.Tests.Fixtures.Ordering.Zulu;

namespace EtAlii.Adp.Editor.Tests;

/// <summary>
/// The editor half of the shared discovery scan (modular-text-editors Requirement 1.3): the
/// same behaviours the diagram family's discovery tests pin, exercised through
/// <see cref="EditorDefinitionDiscovery"/> - because a shared implementation is only shared
/// if both consumers hold it to the same standard.
/// </summary>
[Collection(LogCapture.Collection)]
public class EditorDefinitionDiscoveryTests : IDisposable
{
    private readonly LogCapture _logger = LogCapture.Start();

    public void Dispose() => _logger.Dispose();

    private static FakeAssembly AssemblyWith(string name, params Type[] types) => new(name, types);

    [Fact]
    public void Discover_WithNullAssemblies_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentNullException>(() => EditorDefinitionDiscovery.Discover(null!));
    }

    [Fact]
    public void Discover_FindsAStaticEditorClassWithAPublicStaticDefinition()
    {
        // Arrange and act.
        var result = EditorDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Valid.Editor))]);

        // Assert.
        var definition = Assert.Single(result);
        Assert.Equal("valid", definition.Id);
        Assert.Equal("Valid Fixture", definition.Title);
    }

    [Fact]
    public void Discover_LogsEachDiscoveredEditorWithIdTitleAndAssembly()
    {
        // Act.
        EditorDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Valid.Editor))]);

        // Assert.
        Assert.Contains("Discovered editor valid: Valid Fixture (Fixture.A)", _logger.Informations);
    }

    [Fact]
    public void Discover_ReadsSeveralDefinitionsFromOneClass()
    {
        // Act.
        var result = EditorDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Several.Editor))]);

        // Assert.
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Discover_OrdersByIdOrdinal_RegardlessOfScanOrder()
    {
        // Act: Zulu's assembly is handed in first.
        var result = EditorDefinitionDiscovery.Discover([
            AssemblyWith("Fixture.Z", typeof(Zulu.Editor)),
            AssemblyWith("Fixture.A", typeof(Alpha.Editor)),
        ]);

        // Assert.
        Assert.Equal(new[] { "alpha", "zulu" }, result.Select(definition => definition.Id));
    }

    [Fact]
    public void Discover_DropsADuplicateId_KeepingTheOrdinalSmallerAssembly()
    {
        // Act: both declare id "valid"; Fixture.A wins whichever order they arrive in.
        var result = EditorDefinitionDiscovery.Discover([
            AssemblyWith("Fixture.B", typeof(Duplicate.Editor)),
            AssemblyWith("Fixture.A", typeof(Valid.Editor)),
        ]);

        // Assert.
        var definition = Assert.Single(result);
        Assert.Equal("Valid Fixture", definition.Title);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("declared more than once"));
    }

    [Fact]
    public void Discover_IgnoresAClassThatIsNotStatic()
    {
        // Act.
        var result = EditorDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(NotStatic.Editor))]);

        // Assert: not a candidate at all - no warning, no entry.
        Assert.Empty(result);
    }

    [Fact]
    public void Discover_SkipsAClassWithNoDefinitionsProperty_WithAWarning()
    {
        // Act.
        var result = EditorDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(NoProperty.Editor))]);

        // Assert.
        Assert.Empty(result);
        Assert.Contains(
            "Skipping malformed editor class EtAlii.Adp.Editor.Tests.Fixtures.Malformed.NoProperty.Editor in Fixture.A: it has no public static Definitions property",
            _logger.Warnings);
    }

    [Fact]
    public void Discover_SkipsAWronglyTypedDefinitionsProperty_WithAWarning()
    {
        // Act.
        var result = EditorDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(WrongType.Editor))]);

        // Assert.
        Assert.Empty(result);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("not a sequence of EditorDefinition"));
    }

    [Fact]
    public void Discover_KeepsTheGoodEntries_WhenOneIsNull()
    {
        // Act.
        var result = EditorDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(NullEntry.Editor))]);

        // Assert: one bad entry costs the module only that entry.
        var definition = Assert.Single(result);
        Assert.Equal("kept", definition.Id);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("1 null entry"));
    }

    [Fact]
    public void Discover_WarnsOnAnEmptyDeclaration()
    {
        // Act.
        var result = EditorDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(EmptyArray.Editor))]);

        // Assert.
        Assert.Empty(result);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("declares nothing"));
    }

    [Fact]
    public void Discover_SurvivesAThrowingGetter_WithAWarning()
    {
        // Act.
        var result = EditorDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Throws.Editor))]);

        // Assert: the module costs itself, never the application's startup.
        Assert.Empty(result);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("deliberately broken"));
    }

    [Fact]
    public void Discover_SurvivesAPartiallyLoadableAssembly()
    {
        // Act: the loadable half carries a valid editor; the failed half costs a warning only.
        var result = EditorDefinitionDiscovery.Discover([
            FakeAssembly.PartiallyLoadable("Fixture.A", [typeof(Valid.Editor)], new InvalidOperationException("missing dependency")),
        ]);

        // Assert.
        Assert.Single(result);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("loaded only partially"));
    }

    [Fact]
    public void Discover_SurvivesABrokenAssembly()
    {
        // Act.
        var result = EditorDefinitionDiscovery.Discover([
            FakeAssembly.Broken("Fixture.A", new InvalidOperationException("beyond repair")),
            AssemblyWith("Fixture.B", typeof(Valid.Editor)),
        ]);

        // Assert: the broken one is skipped; the good one still counts.
        Assert.Single(result);
    }

    [Fact]
    public void Discover_LogsASummaryWithCountAndAssembliesScanned()
    {
        // Act.
        EditorDefinitionDiscovery.Discover([
            AssemblyWith("Fixture.A", typeof(Valid.Editor)),
            AssemblyWith("Fixture.B", typeof(Zulu.Editor)),
            AssemblyWith("Fixture.C"),
        ]);

        // Assert.
        Assert.Contains("2 editors discovered across 3 assemblies", _logger.Informations);
    }

    [Fact]
    public void ADefinition_LowerCasesItsExtensionsByConstruction()
    {
        // Arrange and act: a module that types ".TXT" must not defeat lookup (Requirement 2.2).
        var definition = new EditorDefinition("x", "X", Extensions: [".TXT", ".Md"]);

        // Assert.
        Assert.Equal(new[] { ".txt", ".md" }, definition.Extensions);
    }
}
