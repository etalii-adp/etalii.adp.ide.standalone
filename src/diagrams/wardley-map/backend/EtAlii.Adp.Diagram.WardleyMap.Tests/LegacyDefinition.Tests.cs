using System.Text.Json;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The Wardley map's definition as it was before the rewrite at DISL 0.3 (<c>Legacy/wardley-map.dis</c>, see
/// <c>Legacy/provenance.json</c>): findings A1 to A3 of the specification <c>wardley-map-disl-fbl</c>, shown on the
/// file itself, as its amendment 3 rules.
/// </summary>
public class LegacyDefinitionTests
{
    private static async Task<byte[]> Legacy() =>
        await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Legacy", "wardley-map.dis"), TestContext.Current.CancellationToken);

    [Fact]
    public async Task TheDefinitionAtDisl01_IsRefusedByTheLoader()
    {
        // A1: the definition is DISL 0.1. The client reads 0.3 and later only; this loader reads the version and refuses
        // the definition for a call DISL's CEL has no function for.
        var bytes = await Legacy();
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal("0.1", document.RootElement.GetProperty("disl").GetString());

        var result = DislLoader.Load(bytes);

        Assert.Null(result.Specification);
        Assert.Equal(
            ["error at /toolbox/groups/0/tools/7/initial/number/cel: The function 'max' is not supported by this CEL evaluator. (context create)"],
            result.Errors.Select(error => error.ToString()));
    }

    [Fact]
    public async Task ItsMetamodel_HasTheThreeShapesNoBindingCanRead()
    {
        // A3: the url definitions are a list on the diagram, the decorators one list attribute, and an annotation's
        // occurrences a child type, where the body holds a statement each, five separate words and numbers in one statement.
        using var document = JsonDocument.Parse(await Legacy());
        var metamodel = document.RootElement.GetProperty("metamodel");
        var urls = metamodel.GetProperty("diagram").GetProperty("attributes").GetProperty("urls");
        var decorators = metamodel.GetProperty("types").GetProperty("Positioned").GetProperty("attributes").GetProperty("decorators");

        Assert.True(urls.GetProperty("many").GetBoolean());
        Assert.Equal("Decorator", decorators.GetProperty("type").GetString());
        Assert.True(decorators.GetProperty("many").GetBoolean());
        Assert.True(metamodel.GetProperty("types").TryGetProperty("AnnotationPin", out _));
    }
}
