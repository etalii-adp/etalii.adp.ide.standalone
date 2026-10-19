using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Adds a stage, a job or a step, at the end of whatever list it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// One command for all three, because they differ only in which list they join and what text they
/// are - and the toolbox, the context menu and a keyboard shortcut all have to end up executing
/// the same thing (Requirement 9.6).
/// </para>
/// <para>
/// The name is the definition's, a free one among the pipeline's stages or jobs, except when the
/// command names one, which is then made free the same way. Undo and redo land on the same element
/// whatever the name, because they put back bytes.
/// </para>
/// </remarks>
/// <param name="Root">The project.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="Kind">What to add.</param>
/// <param name="ParentId">The stage a job joins, or the job a step joins; empty for a stage.</param>
/// <param name="Name">The name to use, or empty to pick a free one.</param>
public sealed record AddPipelineElementCommand(
    ProjectRoot Root,
    string BodyPath,
    PipelineAddKind Kind,
    string ParentId,
    string Name = "") : IPipelineEditCommand;

internal sealed class AddPipelineElementCommandHandler(IPipelineDocumentStore documents)
    : PipelineEditHandler<AddPipelineElementCommand>(documents)
{
    /// <remarks>
    /// <para>
    /// <b>The definition's operation</b>, <c>addStage</c>, <c>addJob</c>, <c>addDeploymentJob</c> or <c>addStep</c>,
    /// run on the element the command names: its refusals are the definition's, and what it creates (a stage with a
    /// job and a step, a job with a step, a step) is planned as one edit, after the last entry of its list.
    /// </para>
    /// <para>
    /// <b>A stage is added beside the stages, whatever it was asked on</b>: a command that names no parent runs the
    /// operation on the pipeline's first element, which the operation reads only for where it was asked.
    /// </para>
    /// </remarks>
    protected override EditOutcome Apply(PipelineBody body, AddPipelineElementCommand command)
    {
        var diagram = body.Disl.Diagram;
        DislElement? self;
        if (command.ParentId.Length > 0)
        {
            self = PipelineDefinition.ElementOf(body, command.ParentId);
            if (self is null) return EditOutcome.Refused(PipelineDefinition.Gone);

            // Nothing is added inside an element this file does not write: the definition's reason, as for any edit of it.
            if (self.ValueOf("editable") is false) return EditOutcome.Refused(self.ValueOf("readOnlyReason") as string ?? "");
        }
        else
        {
            self = diagram.Nodes.FirstOrDefault(node => node.IsA("Stage") || node.IsA("JobBase") || node.IsA("Step"));

            // A pipeline that holds nothing to ask on, as one that only extends another: the definition's sentence for
            // a pipeline with no stage of its own, which the operation would give were there anything to give it on.
            if (self is null && command.Kind == PipelineAddKind.Stage) return EditOutcome.Refused(NoStages);
        }

        var operation = command.Kind switch
        {
            PipelineAddKind.Stage => "addStage",
            PipelineAddKind.Job => "addJob",
            PipelineAddKind.DeploymentJob => "addDeploymentJob",
            _ => "addStep",
        };

        var taken = Names(diagram, command.Kind);
        var transaction = PipelineDefinition.Run(operation, diagram, self);
        return PipelineDefinition.Apply(body, command.Name.Length > 0 && transaction.WasApplied ? Named(transaction, UnusedName(command.Name, taken)) : transaction);
    }

    /// <summary>The sentence the definition's <c>addStage</c> is unavailable with in a pipeline with no stage of its own.</summary>
    private static string NoStages { get; } = PipelineDefinition.Specification.Root.GetProperty("behavior").GetProperty("operations")
        .GetProperty("addStage").GetProperty("unavailable")[0].GetProperty("message").GetString()!;

    /// <summary>The names a new element of <paramref name="kind"/> must not take: every stage's, or every job's of the pipeline.</summary>
    private static List<string> Names(DislDiagram diagram, PipelineAddKind kind) =>
    [
        .. diagram.NodesOfType(kind == PipelineAddKind.Stage ? "Stage" : "JobBase").Select(node => node.ValueOf("name") as string ?? ""),
    ];

    /// <summary>
    /// <paramref name="transaction"/> with what it created named <paramref name="name"/> in place of the name the
    /// operation chose, and the job of a new stage, and a deployment job's environment, after it as the operation
    /// names them.
    /// </summary>
    /// <remarks>The definition's operations take no name, so a command that brings one renames what the operation made.</remarks>
    private static DislTransaction Named(DislTransaction transaction, string name)
    {
        if (transaction.Changes.OfType<DislChange.Create>().FirstOrDefault()?.Attributes.GetValueOrDefault("name") is not string chosen)
        {
            return transaction;
        }

        Dictionary<string, string> renamed = new(StringComparer.Ordinal)
        {
            [chosen] = name,
            [chosen + "Job"] = name + "Job",
            [chosen.ToLowerInvariant()] = name.ToLowerInvariant(),
        };
        return transaction with
        {
            Changes =
            [
                .. transaction.Changes.Select(change => change is DislChange.Create create
                    ? create with
                    {
                        Attributes = create.Attributes.ToDictionary(
                            attribute => attribute.Key,
                            attribute => attribute.Key is "name" or "environment" && attribute.Value is string value && renamed.TryGetValue(value, out var replaced) ? replaced : attribute.Value,
                            StringComparer.Ordinal),
                    }
                    : change),
            ],
        };
    }

    /// <summary>
    /// <paramref name="wanted"/> if nothing in <paramref name="taken"/> uses it, compared without regard to case,
    /// else <paramref name="wanted"/> followed by 2, 3 and so on: the definition's <c>unusedName</c>.
    /// </summary>
    private static string UnusedName(string wanted, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(wanted)) return wanted;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{wanted}{suffix}";
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
