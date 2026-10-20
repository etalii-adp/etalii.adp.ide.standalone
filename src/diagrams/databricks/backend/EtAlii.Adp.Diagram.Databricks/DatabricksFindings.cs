using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The findings of a Databricks file: the three definitions' findings, each evaluated by the DISL runtime over its own
/// reading (<see cref="DatabricksDefinition.Findings(JobModel, string)"/>), joined into the file's in the order the tool has
/// always reported them (databricks-disl-fbl Requirement 3.3, Q3).
/// </summary>
/// <remarks>
/// <para>
/// <b>This class stands in for language gap 9</b>, for findings A7 and A8, and is deleted when DISL can state both.
/// The codes, severities, sentences and lines are the definitions'; only which readings are judged and the order are
/// code here.
/// </para>
/// <para>
/// <b>The findings belong to the file, not to the reading (A7).</b> The job definition is evaluated once per job of
/// the file, the bundle definition once and the pipeline definition once per pipeline, and the three are joined in
/// that order, whichever tool type asked and whatever resource its registration names. A definition sees one
/// reading, so no definition can say this.
/// </para>
/// <para>
/// <b>Within a job the order is per task (A8)</b>: for each task in the file, its missing key, each of its missing
/// upstream tasks and its missing cluster; then the duplicated keys, a group where its first holder stands; then the
/// tasks that can never run. DISL 8.6 orders by rule and, within one, by model order, and <c>constraints.order</c>
/// sorts by code and then by line, so neither interleaves three rules per element: a task's key finding and cluster
/// finding stand on its own line and a missing upstream on a later one. The order is therefore not hidden in a
/// definition that cannot state it.
/// </para>
/// <para>
/// <b>A file that does not parse has one finding</b>: the reader's <c>std.unparseable</c>, with YamlDotNet's own
/// message as its reason, at the line the parser names.
/// </para>
/// </remarks>
internal static class DatabricksFindings
{
    private const string TaskKeyMissing = "databricks.task-key-missing";
    private const string MissingTask = "databricks.missing-task";
    private const string DanglingCluster = "databricks.dangling-cluster";
    private const string DuplicateTaskKey = "databricks.duplicate-task-key";

    /// <summary>Where a finding of a job stands: per task, then the duplicates, then the rest (the circle) as they arose.</summary>
    private enum Group
    {
        PerTask,
        Duplicates,
        Rest,
    }

    /// <summary>
    /// The findings of the file <paramref name="entry"/> holds, in the order the tool reports them, located in
    /// <paramref name="file"/>, the file's own name.
    /// </summary>
    public static IReadOnlyList<DislFinding> Of(DocumentEntry<LineDocument, DatabricksModel> entry, string file)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(file);
        if (!entry.IsUsable)
        {
            return DatabricksDefinition.Unparseable(entry.Unreadable, (entry.Problems.Count > 0 ? entry.Problems[0].Line : 0) + 1);
        }

        var findings = new List<DislFinding>();
        foreach (var job in entry.Model.Jobs)
        {
            (DislDiagram diagram, IReadOnlyList<DislFinding> evaluated) = DatabricksDefinition.Findings(job, file);
            findings.AddRange(Ordered(job, diagram, evaluated));
        }

        findings.AddRange(DatabricksDefinition.Findings(entry.Model.Bundle, file));
        foreach (var pipeline in entry.Model.Pipelines)
        {
            findings.AddRange(DatabricksDefinition.Findings(pipeline, file));
        }

        return findings;
    }

    /// <summary>
    /// The findings of <paramref name="job"/>, evaluated over <paramref name="diagram"/>, in the tool's order: each
    /// finding's task found by the element it names, its place in the file being the task's among the job's tasks.
    /// </summary>
    private static IEnumerable<DislFinding> Ordered(JobModel job, DislDiagram diagram, IReadOnlyList<DislFinding> findings)
    {
        var places = job.Tasks.Select((task, index) => (Task: task, Index: index)).ToList();
        var keyless = places.Where(place => place.Task.Key.Length == 0).Select(place => place.Index).ToList();
        var keyed = places.Where(place => place.Task.Key.Length > 0).Select(place => place.Index).ToList();

        // One task node per task with a key, in the order of the file.
        var nodes = diagram.NodesOfType("Task");
        var placeOf = new Dictionary<DislElement, int>(ReferenceEqualityComparer.Instance);
        var firstHolder = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < nodes.Count && index < keyed.Count; index++)
        {
            placeOf[nodes[index]] = keyed[index];
            firstHolder.TryAdd(job.Tasks[keyed[index]].Key, keyed[index]);
        }

        var readers = 0;
        var reported = new HashSet<DislElement>(ReferenceEqualityComparer.Instance);
        return findings
            .Select((finding, arising) => (Finding: finding, Arising: arising, Key: KeyOf(finding)))
            .OrderBy(found => found.Key.Group)
            .ThenBy(found => found.Key.Major)
            .ThenBy(found => found.Key.Minor)
            .ThenBy(found => found.Arising)
            .Select(found => found.Finding)
            .ToList();

        (Group Group, int Major, int Minor) KeyOf(DislFinding finding)
        {
            switch (finding.Code)
            {
                case TaskKeyMissing when readers < keyless.Count:
                    return (Group.PerTask, keyless[readers++], 0);
                case MissingTask when Relation(finding) is { Target: { } task } && placeOf.TryGetValue(task, out var place):
                    return (Group.PerTask, place, 1);
                case DanglingCluster when Node(finding) is { } task && placeOf.TryGetValue(task, out var place):
                    return (Group.PerTask, place, 2);
                case DuplicateTaskKey when Node(finding) is { } task && placeOf.TryGetValue(task, out var place):
                    return (Group.Duplicates, firstHolder.GetValueOrDefault(job.Tasks[place].Key, place), place);
                default:
                    return (Group.Rest, 0, 0);
            }
        }

        // A dependency is named by its derived id, which two entries can share: the first not yet reported on its line.
        DislElement? Relation(DislFinding finding)
        {
            var relation = diagram.Relations.FirstOrDefault(candidate =>
                !reported.Contains(candidate) && finding.ElementIds is [var id, ..] && candidate.Id == id && candidate.Line == finding.Line);
            if (relation is not null) reported.Add(relation);
            return relation;
        }

        DislElement? Node(DislFinding finding) =>
            finding.ElementIds is [var id, ..] ? nodes.FirstOrDefault(node => node.Id == id) : null;
    }
}
