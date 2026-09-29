using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `helm/chart`.</summary>
/// <remarks>
/// The second folder-subject type, and deliberately shaped like the first:
/// <para>
/// <b>No document extension.</b> A chart is a directory, so there is no sibling body to name
/// an extension for - the <c>.adp</c> registration inside the chart root is the whole of what
/// ADP contributes, and the diagram is read from the folder around it. Per tech.md, that
/// omission is correct for a folder subject, not a gap to fix.
/// </para>
/// <para>
/// <b><see cref="DiagramSubject.Folder"/>.</b> What hands this module's validator the folder
/// to read, and what makes an edit to a file beneath the registration revalidate this diagram
/// rather than being ignored. Declaring it alongside an extension would be a contradiction,
/// and discovery refuses that pairing.
/// </para>
/// </remarks>
public static class Diagram
{
    /// <summary>
    /// This module's one type, named so the module's own registrations can say which type they
    /// serve without indexing into the array. Discovery reads <see cref="Definitions"/>; the
    /// module reads this.
    /// </summary>
    public static DiagramDefinition HelmCharts { get; } = new(
        new DiagramOrigin("helm", "chart"),
        "Helm chart",
        "What a chart is made of and how it hangs together: metadata, the values layering, "
        + "templates and the kinds they render, declared dependencies and whether charts/ "
        + "actually answers them - drawn from the folder as it is.",
        Icon: "mdi-ship-wheel",
        Subject: DiagramSubject.Folder,
        // Read-only in content; the one edit is a reposition, stored in the .adp itself.
        Build: builder => builder.Services.AddHelmCharts());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [HelmCharts];
}
