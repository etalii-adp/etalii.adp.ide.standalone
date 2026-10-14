using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// <c>token(name)</c> (DISL §12.4): a resolved theme token, in the element and shape contexts. A
/// headless runtime has no theme, so it declares the function, which lets a definition that colours
/// its notation by token load, and reports the token it cannot resolve when one is evaluated.
/// </summary>
public class ThemeTokenTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With());

    [Theory]
    [InlineData(DislContexts.Element)]
    [InlineData(DislContexts.Shape)]
    public void AToken_CompilesInTheContextsThatDrawIt(string context)
    {
        // Act.
        var program = Specification.Environment(context).Compile("token('color.text')");

        // Assert.
        Assert.NotNull(program);
    }

    [Fact]
    public void AToken_IsNotResolvedWithoutATheme()
    {
        // Act.
        var value = Specification.Environment(DislContexts.Element).Compile("token('color.text')").Evaluate(new Dictionary<string, object?>());

        // Assert.
        Assert.Equal(new CelError("The theme token 'color.text' is resolved where the diagram is drawn; this runtime has no theme."), value);
    }

    [Fact]
    public void ADefinitionColouringItsNotationByToken_Loads()
    {
        // Arrange.
        var json = Specifications.With("""
            "metamodel": { "types": { "Thing": {} } },
            "notation": { "nodes": { "Thing": { "shape": "rect", "icon": { "icon": "mdi-sync", "color": { "cel": "token('color.text')" } } } } }
            """);

        // Act.
        var diagnostics = Specifications.Diagnostics(json);

        // Assert.
        Assert.Empty(diagnostics);
    }
}
