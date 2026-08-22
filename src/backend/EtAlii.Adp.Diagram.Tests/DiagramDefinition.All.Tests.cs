using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// These exercise process-wide static state, so they live in one class (xunit runs the tests
/// of a class one at a time) and each one starts from a reset. No other test class touches
/// <see cref="DiagramDefinition.All"/>.
/// </summary>
public class DiagramDefinitionAllTests : IDisposable
{
    private static readonly DiagramDefinition Sample = new(new DiagramOrigin("fixture", "sample"), "Sample");

    public DiagramDefinitionAllTests()
    {
        DiagramDefinition.ResetForTests();
    }

    public void Dispose()
    {
        DiagramDefinition.ResetForTests();
    }

    [Fact]
    public void All_BeforeInitialize_IsEmptyNotNull()
    {
        Assert.NotNull(DiagramDefinition.All);
        Assert.Empty(DiagramDefinition.All);
    }

    [Fact]
    public void Initialize_FillsAll()
    {
        DiagramDefinition.Initialize([Sample]);

        var definition = Assert.Single(DiagramDefinition.All);
        Assert.Same(Sample, definition);
    }

    [Fact]
    public void All_ReturnsTheSameCollectionOnEveryRead()
    {
        // Requirement 1.3: a second read must not re-run anything - it is the cached list.
        DiagramDefinition.Initialize([Sample]);

        Assert.Same(DiagramDefinition.All, DiagramDefinition.All);
    }

    [Fact]
    public void Initialize_CalledTwice_ThrowsAndKeepsTheFirstList()
    {
        DiagramDefinition.Initialize([Sample]);
        var other = new DiagramDefinition(new DiagramOrigin("fixture", "other"), "Other");

        var exception = Assert.Throws<InvalidOperationException>(() => DiagramDefinition.Initialize([other]));

        Assert.Contains("already been initialized", exception.Message, StringComparison.Ordinal);
        Assert.Same(Sample, Assert.Single(DiagramDefinition.All));
    }

    [Fact]
    public void Initialize_WithNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DiagramDefinition.Initialize(null!));
    }

    [Fact]
    public void Initialize_WithAnEmptyList_IsAllowed()
    {
        // "Nothing discovered" is a legitimate, if unfortunate, state - not an error here.
        DiagramDefinition.Initialize([]);

        Assert.Empty(DiagramDefinition.All);
        Assert.Throws<InvalidOperationException>(() => DiagramDefinition.Initialize([Sample]));
    }
}
