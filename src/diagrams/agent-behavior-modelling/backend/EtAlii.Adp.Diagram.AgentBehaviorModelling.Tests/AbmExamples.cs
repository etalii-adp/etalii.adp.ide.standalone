namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>Where the module's own examples are, for every test that reads them.</summary>
/// <remarks>
/// <b>A missing example fails rather than skips</b>, and the path names this module's own folder, so it
/// can never land on <c>src/examples</c> and report success over the wrong corpus.
/// </remarks>
internal static class AbmExamples
{
    /// <summary>The four example names.</summary>
    public static IReadOnlyList<string> Names { get; } = ["pull-request-reviewer", "customer-support", "bug-fixer", "research-assistant"];

    /// <summary>The Markdown body of the example called <paramref name="name"/>.</summary>
    public static string BodyOf(string name) => RepositoryPaths.Under("src", "diagrams", "agent-behavior-modelling", "examples", name, name + ".md");
}
