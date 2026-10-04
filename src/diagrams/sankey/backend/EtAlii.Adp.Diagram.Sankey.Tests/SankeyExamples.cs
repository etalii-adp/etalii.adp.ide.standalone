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

    private static string Find(string folder, string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.Name == "sankey" && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                var path = Path.Combine(directory.FullName, "examples", folder, file);
                return File.Exists(path) ? path : throw new InvalidOperationException($"The {folder} example is missing: {path}");
            }
        }

        throw new InvalidOperationException(
            $"No sankey folder above {AppContext.BaseDirectory}, so the examples cannot be found. That is a failure, not a skip.");
    }
}
