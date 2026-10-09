using EtAlii.Adp.Documents;
using JetBrains.Annotations;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// The agent activity diagram module's entry point: the one diagram type it contributes, found by
/// the startup discovery through <see cref="Definitions"/>.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    /// <summary>The extension of an activity file.</summary>
    public const string DocumentExtension = ".aad";

    /// <summary>Whether <paramref name="path"/> names an activity file.</summary>
    public static bool IsBody(string path) =>
        path is { Length: > 0 } &&
        path.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase);

    public static DiagramDefinition AgentActivity { get; } = new(
        new DiagramOrigin("etalii", "agent-activity-diagram"),
        "Agent activity diagram",
        "Which agents work on which specification of which project, where (a branch and a folder) and on what system, in one picture that agents keep current.",
        Icon: "mdi-account-hard-hat-outline",
        Extension: DocumentExtension,
        // ADP's own notation and ADP's own file: a plain YAML file agents write as they work. The
        // type is specified in etalii-adp/etalii.adp and bundled here; nothing in this module
        // restates what the definition says.
        Build: builder => builder.Services.AddAgentActivityDiagram());

    public static DiagramDefinition[] Definitions { get; } = [AgentActivity];
}
