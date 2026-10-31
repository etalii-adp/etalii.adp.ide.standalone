using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// One document's comparison of the commands with the hand-written writers (databricks-disl-fbl task 12): each edit run
/// both ways on the bytes as they were, and what each gives, a refusal's sentence or the file as the module reads it back,
/// set side by side.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same file as read, not the same bytes</b>: Q1 rules that a value is written in its own span and style, a new
/// key where the binding declares, and a removed entry with its leading comments, so the bytes differ where the writers
/// wrote otherwise. What is compared is the bundle, every job and task and every pipeline as the module reads them.
/// </para>
/// <para>
/// <b>Repairs</b> are listed, not compared, and the test names each one: an edit the writers refused because what it
/// edits is written on one line or in JSON where they looked for a line of YAML (finding B12, the sentence ruled on
/// 2026-10-19), which the command now makes; and one whose written file no longer read (B15), which the command makes
/// or refuses before anything is written.
/// </para>
/// <para>
/// <b>One answer is ruled</b>: setting <c>serverless</c> or <c>edition</c> on a pipeline, which the writers wrote and
/// the commands refuse as the definitions do (fork 2, taken as A on 2026-10-30), is expected refused.
/// </para>
/// <para>
/// <b>And one place is the binding's</b>: a new resource whose kind the file has no level for yet gets that level at the
/// end of <c>resources</c>, where the binding creates it (FBL 0.14 section 6.2, etalii.adp's fixture
/// <c>databricks-resource-add/no-jobs</c>, Peter's "Extend FBL" of 2026-10-30), and the writers wrote it first; so an add of
/// a resource compares the bundle's resources in any order.
/// </para>
/// </remarks>
internal sealed class DatabricksOperationComparison(string name, byte[] original)
{
    private static readonly string[] _ruledRefusals = ["serverless", "edition"];

    /// <summary>Every edit compared, by its label.</summary>
    public List<string> Compared { get; } = [];

    /// <summary>The edits the two answer differently, each with both answers.</summary>
    public List<string> Mismatches { get; } = [];

    /// <summary>The edits the writers refused, or broke the file with, and the commands make.</summary>
    public List<string> Repairs { get; } = [];

    /// <summary>Runs <paramref name="command"/> and <paramref name="handWritten"/> on the document as it was and compares what they give.</summary>
    public async Task CompareAsync(string edit, Func<string, ICommand> command, Func<LineDocument, DatabricksModel, string> handWritten, string? scalar = null, bool resourcesInAnyOrder = false)
    {
        Compared.Add(edit);

        var text = LineDocument.Parse(Encoding.UTF8.GetString(original));
        var refusal = handWritten(text, DatabricksModel.Read(text));
        var written = refusal.Length == 0 ? DatabricksModel.Outcome(LineDocument.Parse(text.Text)) : null;

        using var editing = DatabricksEditing.Copying(DatabricksParityCorpus.PathOf(name));
        var result = await editing.RunAsync(command);
        var after = result.IsSuccess ? DatabricksModel.Outcome(LineDocument.Parse(await editing.TextAsync())) : null;
        var actual = after is null ? $"refused: {result.Error}" : Answer(after, resourcesInAnyOrder);

        // A repair: the writers refused the edit, or wrote a file that no longer read, and the command makes it, or refuses
        // it before anything is written.
        if ((refusal.Length > 0 && after is { IsUsable: true }) || (written is { IsUsable: false } && after is null or { IsUsable: true }))
        {
            Repairs.Add(edit);
            return;
        }

        if (scalar is not null && _ruledRefusals.Contains(scalar, StringComparer.Ordinal) && !result.IsSuccess)
        {
            return;
        }

        var expected = refusal.Length > 0 ? $"refused: {refusal}" : Answer(written!, resourcesInAnyOrder);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Mismatches.Add($"{edit}\n  hand-written {Indented(expected)}\n  command      {Indented(actual)}");
        }
    }

    private static string Answer(ParseOutcome<DatabricksModel> outcome, bool resourcesInAnyOrder) =>
        outcome.IsUsable ? Read(outcome.Model, resourcesInAnyOrder) : $"a file that does not read: {outcome.Unreadable}";

    private static string Indented(string text) => text.Replace("\n", "\n               ", StringComparison.Ordinal);

    /// <summary>The file as it is compared: the bundle, every job with its tasks and every pipeline with its libraries, one line each.</summary>
    private static string Read(DatabricksModel model, bool resourcesInAnyOrder)
    {
        var lines = new StringBuilder();
        var resources = model.Bundle.Resources.Select(resource => $"{resource.Kind}/{resource.Key}");
        if (resourcesInAnyOrder) resources = resources.Order(StringComparer.Ordinal);
        lines.Append($"bundle '{model.Bundle.Name}' {string.Join(" ", resources)}\n");
        foreach (var job in model.Jobs)
        {
            lines.Append($"job {job.Key} '{job.Name}' clusters {string.Join(",", job.Clusters.Select(cluster => cluster.Key))}\n");
            foreach (var task in job.Tasks)
            {
                var dependencies = string.Join(",", task.DependsOn.Select(dependency => $"{dependency.TaskKey}:{dependency.Outcome}"));
                lines.Append($"  task '{task.Key}' {task.Type} '{task.Source}' cluster '{task.ClusterKey}' run_if '{task.RunIf}' after [{dependencies}]\n");
            }
        }

        foreach (var pipeline in model.Pipelines)
        {
            lines.Append($"pipeline {pipeline.Key} '{pipeline.Name}' '{pipeline.Catalog}' '{pipeline.Schema}' '{pipeline.Channel}' {pipeline.Serverless} {pipeline.Continuous} {pipeline.Development}\n");
            foreach (var library in pipeline.Libraries)
            {
                lines.Append($"  library {library.Kind} '{library.Path}'\n");
            }
        }

        return lines.ToString();
    }
}
