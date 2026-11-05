using System.Reflection;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The module's own binding, <c>structurizr.fbl</c>, embedded (c4-disl-fbl Requirement 4.1, design
/// Amendment 2): it loads with no problem, and it reads every kind of element the definitions model.
/// </summary>
/// <remarks>
/// The binding is on no reading path yet: <c>C4Parser</c> reads every body, which the parity
/// transcript holds. These tests load it and read no body through it.
/// </remarks>
public class C4BindingTests
{
    private static readonly Assembly _module = typeof(Diagram).Assembly;

    [Fact]
    public void TheEmbeddedBinding_LoadsWithItsGrammarAndNoErrorAndNoWarningButItsVersion()
    {
        // Act: loaded as a module loads it, with the grammar the module embeds beside it.
        var problems = BundledBinding.Load(_module, "structurizr.fbl", out var document);

        // Assert: the grammar builds, and the one problem is the warning that the binding is of a later FBL than the
        // library reads (FBL 2.7; etalii.adp spec 072 Requirement 8.1: such a binding loads, with that warning).
        var warning = Assert.Single(problems);
        Assert.Equal(LoadSeverity.Warning, warning.Severity);
        Assert.Equal("/fbl", warning.Pointer);
        Assert.True(document!.Bindings.ContainsKey("workspace"));
    }

    [Fact]
    public void TheBinding_HasARuleForEveryElementTypeOfTheModel()
    {
        // Arrange: the six definitions share one metamodel, which BundledDefinitionTests holds equal.
        var types = _module.ResourceJson("c4-container.dis").GetProperty("metamodel").GetProperty("types")
            .EnumerateObject()
            .Where(type => !(type.Value.TryGetProperty("abstract", out var isAbstract) && isAbstract.GetBoolean()))
            .Select(type => type.Name)
            .ToList();

        // Act.
        var binding = BundledBinding.Load(_module, "structurizr.fbl", "workspace");
        var bound = binding.Elements.Select(rule => rule.Type).ToHashSet(StringComparer.Ordinal);

        // Assert: all eight kinds and the included file (findings B1 and B11).
        Assert.NotEmpty(types);
        Assert.All(types, type => Assert.Contains(type, bound));
    }
}
