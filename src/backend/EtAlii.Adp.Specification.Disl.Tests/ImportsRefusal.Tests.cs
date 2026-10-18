using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The runtime does not resolve <c>imports</c> (DISL §3.3), and refuses a specification that declares
/// any with one sentence. A change of that, a later ruling to resolve them, is a change of this test.
/// </summary>
public class ImportsRefusalTests
{
    [Fact]
    public void ASpecificationThatImports_IsRefusedWithTheSentenceThatSaysSo()
    {
        // Arrange.
        var json = Specifications.With("""
            "imports": [ { "uri": "shared.dis" } ]
            """);

        // Act.
        var result = DislLoader.Load(json);

        // Assert.
        Assert.Null(result.Specification);
        Assert.Equal(["error at /imports: This runtime does not resolve imports (DISL §3.3, step 3 of §14.1)."], Specifications.Diagnostics(json));
    }

    [Fact]
    public void ASpecificationWithAnEmptyListOfImports_Loads()
    {
        // Act.
        var result = DislLoader.Load(Specifications.With("""
            "imports": []
            """));

        // Assert.
        Assert.NotNull(result.Specification);
    }
}
