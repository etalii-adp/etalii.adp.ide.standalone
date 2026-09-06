using EtAlii.Adp.Common;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// The definition (Requirement 2.1) and the registration (Requirement 12.1): the folder
/// subject declared, the extension deliberately absent, the services resolvable.
/// </summary>
public class DiagramTests
{
    [Fact]
    public void TheDefinition_DeclaresAFolderSubjectAndNoExtension()
    {
        // Assert.
        var definition = Diagram.HelmCharts;
        Assert.Equal("helm", definition.Origin.Vendor);
        Assert.Equal("chart", definition.Origin.Type);
        Assert.Equal("helm/chart", definition.Origin.Key);
        Assert.Equal(DiagramSubject.Folder, definition.Subject);
        Assert.True(definition.HasFolderSubject);
        // No extension is the folder subject's correct shape (tech.md), not a gap.
        Assert.True(string.IsNullOrEmpty(definition.Extension));
        Assert.NotNull(definition.Build);
        Assert.False(string.IsNullOrEmpty(definition.Icon));
        Assert.Same(definition, Assert.Single(Diagram.Definitions));
    }

    [Fact]
    public void AddHelmCharts_MakesTheSeamsResolvable()
    {
        // Arrange.
        var services = new ServiceCollection();

        // Act.
        services.AddHelmCharts();
        using var provider = services.BuildServiceProvider();

        // Assert.
        Assert.NotNull(provider.GetRequiredService<HelmChartReader>());
        Assert.NotNull(provider.GetRequiredService<IHelmChartStore>());
        Assert.Same(provider.GetRequiredService<HelmChartStore>(), provider.GetRequiredService<IHelmChartStore>());
        Assert.NotNull(provider.GetRequiredService<HelmElementMapper>());
        // The session factory and validator, asserted as DESCRIPTORS (the factory needs core
        // services to construct): present and correctly typed. A silent no-op edit once left
        // both unregistered while every other test stayed green - this is that guard.
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(EtAlii.Adp.Diagram.IDiagramSessionFactory)
            && descriptor.ImplementationType == typeof(HelmSessionFactory));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDiagramValidator)
            && descriptor.ImplementationType == typeof(HelmValidator));
    }

    [Fact]
    public void TheModule_RegistersNoWritingSeams()
    {
        // Arrange.
        var services = new ServiceCollection();

        // Act.
        services.AddHelmCharts();

        // Assert.
        // The absences are the statement (Requirement 9): no document factory, no toolbox,
        // no action provider, no command handlers - asserted so a later addition is a
        // deliberate decision, not a drift.
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.Name.Contains("DocumentFactory", StringComparison.Ordinal)
            || descriptor.ServiceType.Name.Contains("Toolbox", StringComparison.Ordinal)
            || descriptor.ServiceType.Name.Contains("ActionProvider", StringComparison.Ordinal)
            || descriptor.ServiceType.Name.Contains("CommandHandler", StringComparison.Ordinal));
    }
}
