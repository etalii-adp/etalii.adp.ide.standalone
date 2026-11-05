namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>Where the module's own examples are, for every test that reads them.</summary>
/// <remarks>
/// <b>A missing example FAILS rather than skips</b>: the walk anchors on this module's own folder by
/// name, so it cannot climb to <c>src/examples</c> and find another corpus while reporting success.
/// </remarks>
internal static class SankeyExamples
{
    /// <summary>The fictional company's income statement.</summary>
    public static string BrightwaterCoffee => Find("brightwater-coffee", "brightwater-coffee.skv");

    /// <summary>The UK energy flows.</summary>
    public static string UkEnergy => Find("uk-energy", "uk-energy.skv");

    /// <summary>The recent US graduates.</summary>
    public static string RecentGraduates => Find("recent-graduates", "recent-graduates.skv");

    private static string Find(string folder, string file) => RepositoryPaths.Under("src", "diagrams", "sankey", "examples", folder, file);
}
