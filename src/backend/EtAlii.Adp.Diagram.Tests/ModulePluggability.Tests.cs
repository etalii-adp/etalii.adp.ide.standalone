using System.Reflection;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// The "core stays testable with zero diagram modules" property (mindmap-diagram Requirement 13
/// NFR), exercised by the test project of the seam every module plugs into. This project
/// references only EtAlii.Adp.Diagram, so the assembly inspected is one whose purpose is not
/// modules (code-structure T310). The check is on compiled metadata: a module type used here
/// puts that module among this assembly's references and fails it.
/// </summary>
public class ModulePluggabilityTests
{
    [Fact]
    public void Core_CompilesAndRuns_WithNoDiagramModuleLoaded()
    {
        // Arrange and act.
        // EtAlii.Adp.Diagram is the seam every module plugs into. That it loads and its core
        // types resolve without any module present is exercised by this very assembly: these
        // unit tests reference core, not the mindmap module.
        var thisAssembly = Assembly.GetExecutingAssembly();
        var references = thisAssembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        // Assert.
        Assert.DoesNotContain("EtAlii.Adp.Diagram.Mindmap", references);
        // And the core definition type is usable here with no module in sight.
        Assert.Equal("", new DiagramDefinition(new ToolOrigin("x", "y"), "Z").Extension);
    }
}
