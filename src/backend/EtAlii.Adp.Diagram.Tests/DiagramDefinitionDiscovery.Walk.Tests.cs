using System.Reflection;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// Exercises the assembly walk against this test process's real entry assembly and real
/// deployment manifest - the one place the manifest seeding can be proven rather than mocked.
/// </summary>
public class DiagramDefinitionDiscoveryWalkTests
{
    [Fact]
    public void FindApplicationAssemblies_ReturnsOnlyAssembliesWithTheAdpPrefix()
    {
        var assemblies = DiagramDefinitionDiscovery.FindApplicationAssemblies();

        Assert.NotEmpty(assemblies);
        Assert.All(assemblies, assembly =>
            Assert.StartsWith(DiagramDefinitionDiscovery.AssemblyPrefix, assembly.GetName().Name, StringComparison.Ordinal));
    }

    [Fact]
    public void FindApplicationAssemblies_ReachesAssembliesTheEntryAssemblyNeverReferences()
    {
        // Under the test runner the entry assembly is the test host, which carries no
        // metadata reference to anything of ours - exactly the situation the real host is in
        // with the diagram modules (Requirement 3.3). Finding this very assembly therefore
        // proves the walk is seeded from the deployment manifest; the unseeded walk from the
        // article returns nothing here.
        var entry = Assembly.GetEntryAssembly();
        var entryReferencesUs = entry?.GetReferencedAssemblies()
            .Any(reference => reference.Name?.StartsWith(DiagramDefinitionDiscovery.AssemblyPrefix, StringComparison.Ordinal) == true) ?? false;
        Assert.False(entryReferencesUs, "precondition: the entry assembly must not reference EtAlii.Adp.* for this test to prove anything");

        var names = DiagramDefinitionDiscovery.FindApplicationAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("EtAlii.Adp.Diagram.Tests", names);
        Assert.Contains("EtAlii.Adp.Diagram", names);
        Assert.Contains("EtAlii.Adp", names);
    }

    [Fact]
    public void FindApplicationAssemblies_FindsAssembliesNoMetadataReferenceLeadsTo()
    {
        // EtAlii.Adp.Diagram has a <ProjectReference> to EtAlii.Adp, yet its compiled metadata
        // records no reference to it, because no type from EtAlii.Adp is used - the very
        // pruning Requirement 3.3 measured on the host. The walk still returns EtAlii.Adp,
        // which it can only have got from the manifest. If this precondition ever flips
        // (someone uses an EtAlii.Adp type in the Diagram library), the test below still
        // holds; only this one's documentary value is lost, so it is asserted softly.
        var assemblies = DiagramDefinitionDiscovery.FindApplicationAssemblies();
        var diagram = Assert.Single(assemblies, a => a.GetName().Name == "EtAlii.Adp.Diagram");

        var metadataReferencesCore = diagram.GetReferencedAssemblies().Any(r => r.Name == "EtAlii.Adp");

        Assert.Contains(assemblies, a => a.GetName().Name == "EtAlii.Adp");
        Assert.False(
            metadataReferencesCore,
            "EtAlii.Adp.Diagram now carries a metadata reference to EtAlii.Adp; this test no longer demonstrates the manifest seed on its own");
    }

    [Fact]
    public void FindApplicationAssemblies_DoesNotReturnTheTestHostEntryAssembly()
    {
        // The entry assembly is where the walk starts, but under a test runner it is the
        // runner's own host - not ours, so not something to scan for diagram types.
        var names = DiagramDefinitionDiscovery.FindApplicationAssemblies().Select(a => a.GetName().Name);

        Assert.DoesNotContain("testhost", names);
    }

    [Fact]
    public void FindApplicationAssemblies_ReturnsEachAssemblyOnce()
    {
        var names = DiagramDefinitionDiscovery.FindApplicationAssemblies()
            .Select(assembly => assembly.FullName)
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void FindApplicationAssemblies_WithALogger_DoesNotWarnOnAHealthyDeployment()
    {
        var logger = new ListLogger<DiagramDefinitionDiscovery>();

        DiagramDefinitionDiscovery.FindApplicationAssemblies(logger);

        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void DiscoverOverTheWalk_FindsThisAssemblysFixtures()
    {
        // The end-to-end shape the host uses: walk, then discover. The fixtures live here, so
        // a walk that really reaches this assembly yields them.
        var logger = new ListLogger<DiagramDefinitionDiscovery>();
        var discovery = new DiagramDefinitionDiscovery(logger);

        var result = discovery.Discover(DiagramDefinitionDiscovery.FindApplicationAssemblies(logger));

        Assert.Contains(result, d => d.Origin.Key == "fixture/valid");
        Assert.Contains(result, d => d.Origin.Key == "alpha/z");
    }
}
