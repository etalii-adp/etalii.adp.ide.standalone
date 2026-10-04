namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>Where the module's own examples are, for every test that reads them.</summary>
/// <remarks>
/// <b>A missing example fails rather than skips</b>, and the walk anchors on this module's own folder
/// by name, so it can never climb to <c>src/examples</c> and report success over the wrong corpus.
/// </remarks>
internal static class AbmExamples
{
    /// <summary>The four example names.</summary>
    public static IReadOnlyList<string> Names { get; } = ["pull-request-reviewer", "customer-support", "bug-fixer", "research-assistant"];

    /// <summary>The Markdown body of the example called <paramref name="name"/>.</summary>
    public static string BodyOf(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.Name == "agent-behavior-modelling")
            {
                var path = Path.Combine(directory.FullName, "examples", name, name + ".md");
                return File.Exists(path) ? path : throw new InvalidOperationException($"The {name} example is missing: {path}");
            }
        }

        throw new InvalidOperationException($"No agent-behavior-modelling folder above {AppContext.BaseDirectory}. That is a failure, not a skip.");
    }
}
