using EtAlii.Adp.Common;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// <see cref="DiagramDefinition.Description"/> defaults to empty and nothing here rejects that
/// default, because this project has no access to real diagram modules to check - it references
/// only core (see the project's own remark on why: pluggability forbids core from naming a
/// module, and a project reference the other way round exists only to check every module, which
/// is what <c>DiagramDiscoveryStartupTests</c> does instead, against the real deployed set).
/// </summary>
/// <remarks>
/// This file used to assert "every discovered definition carries a description" here, against
/// <c>DiagramDefinition.All</c> - a process-wide static cache removed when discovery moved to
/// <see cref="IDiagramDefinitionCatalog"/>. What replaced it in commit 59304658 was a single
/// local fixture with no description, which asserted a vacuous truth about itself rather than
/// checking anything discovered. The real check now lives in
/// <c>EtAlii.Adp.Backend.Tests/Integration Tests/DiagramDiscoveryStartup.Tests.cs</c>, which
/// builds the real host and can see every module it deploys.
/// </remarks>
public class DiagramDefinitionDescriptionTests
{
    [Fact]
    public void ADefinitionWithNoDescription_DefaultsToEmpty()
    {
        // Arrange & act.
        var sample = new DiagramDefinition(new DiagramOrigin("fixture", "sample"), "Sample");

        // Assert.
        Assert.Equal("", sample.Description);
    }
}
