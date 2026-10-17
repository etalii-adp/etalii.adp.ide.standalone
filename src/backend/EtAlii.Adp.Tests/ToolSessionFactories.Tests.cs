using Xunit;

namespace EtAlii.Adp.Tests;

/// <summary>The one session-factory registry of the three tool kinds (code-structure FR-057, T166).</summary>
public class ToolSessionFactoriesTests
{
    [Fact]
    public void Find_ReturnsTheFactoryServingTheKey()
    {
        // Arrange.
        Factory sheet = new("sheet"), form = new("form");
        var factories = new Factories([sheet, form]);

        // Act and assert.
        Assert.Same(form, factories.Find("form"));
        Assert.Same(sheet, factories.Find("sheet"));
    }

    [Fact]
    public void Find_WithAKeyNoFactoryServes_ReturnsNull()
    {
        // Arrange.
        var factories = new Factories([new Factory("sheet")]);

        // Act and assert.
        Assert.Null(factories.Find("form"));
    }

    [Fact]
    public void Find_ComparesKeysWithTheKindsComparer()
    {
        // Arrange: an ordinal registry does not find "Sheet" for "sheet"; an ignore-case one does.
        Factory sheet = new("sheet");
        var ordinal = new Factories([sheet], StringComparer.Ordinal);
        var ignoreCase = new Factories([sheet], StringComparer.OrdinalIgnoreCase);

        // Act and assert.
        Assert.Null(ordinal.Find("Sheet"));
        Assert.Same(sheet, ignoreCase.Find("Sheet"));
    }

    [Fact]
    public void Constructing_WithTwoFactoriesServingOneKey_Throws()
    {
        // Act and assert.
        Assert.Throws<ArgumentException>(() => new Factories([new Factory("sheet"), new Factory("sheet")]));
    }

    private sealed record Factory(string Key);

    private sealed class Factories(IEnumerable<Factory> factories, IEqualityComparer<string>? comparer = null)
        : ToolSessionFactories<string, Factory>(factories, factory => factory.Key, comparer);
}
