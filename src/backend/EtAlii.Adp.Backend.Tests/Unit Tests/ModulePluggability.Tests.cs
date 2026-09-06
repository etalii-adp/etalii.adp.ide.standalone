using System.Reflection;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Proves the claim mindmap-diagram Requirement 13 makes rather than asserting it: core does
/// not depend on the mindmap module. The check is on compiled metadata, so a reference added
/// in source - even one a comment says is fine - fails it; a comment cannot fool it.
/// </summary>
public class ModulePluggabilityTests
{
    private const string ModulePrefix = "EtAlii.Adp.Diagram.";

    /// <summary>The core assemblies a diagram module must never appear in the reference graph of.</summary>
    [Theory]
    [InlineData("EtAlii.Adp")]
    [InlineData("EtAlii.Adp.Diagram")]
    [InlineData("EtAlii.Adp.Backend")]
    public void ACoreAssembly_ReferencesNoDiagramModule(string coreAssemblyName)
    {
        // Arrange.
        var core = Assembly.Load(coreAssemblyName);

        // Act.
        var moduleReferences = core.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith(ModulePrefix, StringComparison.Ordinal))
            .ToArray();

        // Assert.
        Assert.True(
            moduleReferences.Length == 0,
            $"{coreAssemblyName} references diagram module(s): {string.Join(", ", moduleReferences)}. " +
            "Core must not depend on any diagram module (Requirement 13.4).");
    }

    [Fact]
    public void TheMindmapModule_DependsOnCore_NotTheOtherWayRound()
    {
        // Arrange.
        // The dependency exists - it just runs the right way. This is what makes the test above
        // meaningful rather than vacuously true because nothing references anything.
        var mindmap = Assembly.Load("EtAlii.Adp.Diagram.Mindmap");

        // Act.
        var referencedNames = mindmap.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        // Assert.
        Assert.Contains("EtAlii.Adp.Backend", referencedNames);
        Assert.Contains("EtAlii.Adp.Diagram", referencedNames);
    }

    [Fact]
    public void Core_CompilesAndRuns_WithNoDiagramModuleLoaded()
    {
        // Arrange and act.
        // EtAlii.Adp.Diagram is the seam every module plugs into. That it loads and its core
        // types resolve without any module present is the "core stays testable with zero
        // diagram modules" property (Requirement 13 NFR), exercised by this very assembly:
        // these unit tests reference core, not the mindmap module.
        var thisAssembly = Assembly.GetExecutingAssembly();
        var references = thisAssembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        // Assert.
        Assert.DoesNotContain("EtAlii.Adp.Diagram.Mindmap", references);
        // And the core definition type is usable here with no module in sight.
        Assert.Equal("", new DiagramDefinition(new DiagramOrigin("x", "y"), "Z").Extension);
    }
}
