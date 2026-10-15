using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// The findings the definition derives, as the host is handed them: in today's order and in today's words.
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for two language gaps, and nothing a rule can state</b> (ansible-structure-disl-fbl,
/// Requirements 3.2 and 3.3; gaps 6 and 8 of the definition's companion, <c>definition/ansible-structure.md</c>).
/// It goes when DISL gives <c>std.unparseable</c>'s message the file and lets an order follow the model's
/// relations.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The order</b> (gap 8, finding A12). The runtime lists the reader's findings, then each rule's in
/// declaration order. The tool lists the findings about relations in relation order, whichever of the two
/// missing-target rules raised them, so each run of findings about relations is put in the model's relation
/// order; everything else keeps the runtime's order.
/// </item>
/// <item>
/// <b>The sentence of a file that does not parse</b> (gap 6, finding A8; the amendment ruled on 2026-10-11).
/// DISL gives that message the reason alone; the tool's sentence opens with the file, so it is composed here
/// from the finding's file and its reason.
/// </item>
/// <item>
/// <b>A file that is not there is no finding</b> (finding B5). A <c>std.unparseable</c> naming no file of the
/// folder is not passed on, so a folder that is not there opens empty and says nothing.
/// </item>
/// </list>
/// </remarks>
internal static class AnsibleFindings
{
    /// <summary><paramref name="findings"/> of <paramref name="diagram"/> as the host's problems, located relative to the folder.</summary>
    public static IReadOnlyList<DiagramProblem> For(DislDiagram diagram, IReadOnlyList<DislFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(findings);

        var relations = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < diagram.Relations.Count; index++)
        {
            relations.TryAdd(diagram.Relations[index].Id, index);
        }

        var passed = findings
            .Where(finding => finding.ConstraintId != DislReaderFinding.Unparseable || finding.File is { Length: > 0 })
            .ToList();

        var ordered = new List<DislFinding>(passed.Count);
        for (var at = 0; at < passed.Count;)
        {
            if (RelationOf(passed[at], relations) is null)
            {
                ordered.Add(passed[at++]);
                continue;
            }

            var run = new List<DislFinding>();
            while (at < passed.Count && RelationOf(passed[at], relations) is not null)
            {
                run.Add(passed[at++]);
            }

            ordered.AddRange(run.OrderBy(finding => RelationOf(finding, relations)));
        }

        return [.. ordered.Select(Problem)];
    }

    /// <summary>Where in the model's relations the finding's element is; null for a finding about no relation.</summary>
    private static int? RelationOf(DislFinding finding, Dictionary<string, int> relations) =>
        finding.ElementIds is [var id, ..] && relations.TryGetValue(id, out var index) ? index : null;

    private static DiagramProblem Problem(DislFinding finding)
    {
        var message = finding.ConstraintId == DislReaderFinding.Unparseable
            ? $"'{finding.File}' is not readable YAML: {finding.Message}"
            : finding.Message;
        var location = finding.File is { } file
            ? new DiagramProblemFileLocation(file, finding.Line is { } line ? (uint)line : 0)
            : null;
        return new DiagramProblem(Severity(finding.Severity), message, finding.Code, location);
    }

    private static DiagramProblemSeverity Severity(string severity) => severity switch
    {
        "error" => DiagramProblemSeverity.Error,
        "warning" => DiagramProblemSeverity.Warning,
        _ => DiagramProblemSeverity.Info,
    };
}
