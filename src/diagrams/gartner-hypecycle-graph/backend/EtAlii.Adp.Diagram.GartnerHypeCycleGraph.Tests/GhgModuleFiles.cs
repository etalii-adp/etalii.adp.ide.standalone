namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>Where this module's own example and scale fixture are, for every test that reads them.</summary>
/// <remarks>
/// <b>A missing file FAILS rather than skips</b>, as FDG's locator does: this anchors on the module's
/// own folder by name, not on the first folder above the test binary that holds an examples directory.
/// </remarks>
internal static class GhgModuleFiles
{
    /// <summary>The technology-trends example's <c>.ghg</c> body.</summary>
    public static string Example => ExampleNamed("technology-trends");

    /// <summary>The <c>.ghg</c> body of the example in <c>examples/<paramref name="name"/>/</c>.</summary>
    public static string ExampleNamed(string name) => Existing(Path.Combine(ModuleFolder, "examples", name, name + ".ghg"));

    /// <summary>The scale fixture the backend and the client both assert against.</summary>
    public static string ScaleFixture => Existing(Path.Combine(ModuleFolder, "scale-fixture.json"));

    private static string ModuleFolder
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (directory.Name == "gartner-hypecycle-graph")
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException(
                $"No gartner-hypecycle-graph folder above {AppContext.BaseDirectory}. That is a failure, not a skip.");
        }
    }

    private static string Existing(string path) =>
        File.Exists(path) ? path : throw new InvalidOperationException($"A module file is missing: {path}");
}
