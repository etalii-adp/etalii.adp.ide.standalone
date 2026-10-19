namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The findings the module reports for a workspace, as its validators do: derived from the C4
/// definitions (<see cref="C4Definitions.Problems"/>), the same under every origin.
/// </summary>
internal static class C4Findings
{
    /// <summary>The file name a test's workspace is read from.</summary>
    private const string File = "workspace.dsl";

    public static IReadOnlyList<DiagramProblem> Of(C4Workspace workspace) => C4Definitions.Problems(workspace, File, Diagram.SystemContext.Origin);

    /// <summary>What <paramref name="view"/> shows, derived from the definition of its kind.</summary>
    public static IReadOnlyList<C4Element> MembersOf(C4Workspace workspace, C4View view) => C4Definitions.Drawing(workspace, File, view).Members;
}
