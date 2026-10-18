using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// The palette, the context menus and the property rows as the module's providers wrote them by hand
/// before they were derived from the bundled DISL definition (azure-devops-pipeline-disl-fbl task 6),
/// kept as the oracle the derived answers are compared with, document by document and element by
/// element. Moved here unchanged but for the shape of the calls: what the providers asked of a target and
/// its connection's view is passed in.
/// </summary>
internal static class HandWrittenPipeline
{
    private const string RenameActionId = PipelineContextActionProvider.RenameActionId;
    private const string ToggleEnabledActionId = PipelineContextActionProvider.ToggleEnabledActionId;
    private const string AddStageActionId = PipelineContextActionProvider.AddStageActionId;
    private const string AddJobActionId = PipelineContextActionProvider.AddJobActionId;
    private const string AddDeploymentJobActionId = PipelineContextActionProvider.AddDeploymentJobActionId;
    private const string AddStepActionId = PipelineContextActionProvider.AddStepActionId;
    private const string RemoveActionId = PipelineContextActionProvider.RemoveActionId;
    private const string MoveStepUpActionId = PipelineContextActionProvider.MoveStepUpActionId;
    private const string MoveStepDownActionId = PipelineContextActionProvider.MoveStepDownActionId;
    private const string ClearDependenciesActionId = PipelineContextActionProvider.ClearDependenciesActionId;
    private const string ToggleStageActionId = PipelineContextActionProvider.ToggleStageActionId;
    private const string ToggleJobActionId = PipelineContextActionProvider.ToggleJobActionId;

    private const string DisplayNamePropertyId = PipelineContextPropertyProvider.DisplayNamePropertyId;
    private const string DependsOnPropertyId = PipelineContextPropertyProvider.DependsOnPropertyId;
    private const string EnabledPropertyId = PipelineContextPropertyProvider.EnabledPropertyId;
    private const string IdentityGroup = "Identity";
    private const string OrderingGroup = "Ordering";
    private const string ExecutionGroup = "Execution";
    private const string NothingCandidate = PipelineContextPropertyProvider.NothingCandidate;
    private const string DefaultCandidate = PipelineContextPropertyProvider.DefaultCandidate;
    private const string EditItInTheFile = "This is edited in the pipeline file. A wrong value here would break the build.";

    /// <summary>The palette (<c>PipelineToolboxProvider.Items</c>).</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox { get; } =
    [
        new(
            "azure-pipeline.toolbox.stage",
            "Stage",
            "mdi-layers-plus",
            "Drop on a stage to add another after it. It arrives with a job and a step, so it runs.",
            AddStageActionId),
        new(
            "azure-pipeline.toolbox.job",
            "Job",
            "mdi-plus-box-outline",
            "Drop on a stage to add a job to it.",
            AddJobActionId),
        new(
            "azure-pipeline.toolbox.deployment-job",
            "Deployment job",
            "mdi-rocket-launch-outline",
            "Drop on a stage to add a deployment job, with the environment and strategy one needs.",
            AddDeploymentJobActionId),
        new(
            "azure-pipeline.toolbox.step",
            "Script step",
            "mdi-console-line",
            "Drop on a job to add a script step to it.",
            AddStepActionId),
    ];

    /// <summary>
    /// The problems of <paramref name="text"/> as the validator judged them by hand
    /// (<c>PipelineValidator.ValidateAsync</c>): the one problem naming the line when it does not parse,
    /// else <see cref="PipelineRuleSet"/>'s over the model read without following a template.
    /// </summary>
    public static IReadOnlyList<DiagramProblem> Findings(string text)
    {
        var outcome = PipelineParser.Outcome(LineDocument.Parse(text));
        if (!outcome.IsUsable)
        {
            var problem = outcome.Problems[0];
            return
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"This is not YAML that can be read: {problem.Message}",
                    PipelineValidator.UnparseableRuleId,
                    new DiagramProblemLineLocation((uint)problem.Line + 1)),
            ];
        }

        return PipelineRuleSet.Judge(outcome.Model);
    }

    /// <summary>The context menu of <paramref name="elementId"/>, open as <paramref name="view"/> has it (<c>PipelineContextActionProvider.Discover</c>).</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<PipelineBody, PipelineModel> entry, PipelineConnectionView view, string elementId)
    {
        var groups = Menus(entry, elementId);
        if (groups.Count == 0 || Resolve(entry, elementId) is not { } location || OpenerOf(location) is not { } inside)
        {
            return groups;
        }

        var open = view.IsExpanded(inside.Id);
        var toggle = new ContextActionDefinition(
            inside.ActionId,
            open ? inside.Hide : inside.Show,
            open ? "mdi-unfold-less-horizontal" : "mdi-unfold-more-horizontal",
            new ContextShortcutDefinition(" "));

        return [new ContextActionGroupDefinition([toggle, .. groups[0].Actions]), .. groups.Skip(1)];
    }

    /// <summary>What the element offers whatever is open.</summary>
    private static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<PipelineBody, PipelineModel> entry, string elementId)
    {
        if (Resolve(entry, elementId) is not { } location || !location.Target.IsEditable)
        {
            return [];
        }

        var edits = new List<ContextActionDefinition>
        {
            new(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new(
                ToggleEnabledActionId,
                Execution(location).IsDisabled ? "Enable" : "Disable",
                Execution(location).IsDisabled ? "mdi-play-circle-outline" : "mdi-pause-circle-outline"),
        };

        if (location.Kind != PipelineElementLocationKind.Step && DependsOnDeclared(location))
        {
            edits.Add(new ContextActionDefinition(ClearDependenciesActionId, "Use the default order", "mdi-arrow-decision-outline"));
        }

        if (Removable(entry.Model, location))
        {
            edits.Add(new ContextActionDefinition(RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")));
        }

        var additions = AdditionsFor(location);
        var ordering = OrderingFor(location);
        return new[]
            {
                new ContextActionGroupDefinition(edits),
                additions.Count > 0 ? new ContextActionGroupDefinition(additions) : null,
                ordering.Count > 0 ? new ContextActionGroupDefinition(ordering) : null,
            }
            .OfType<ContextActionGroupDefinition>()
            .ToArray();
    }

    private static List<ContextActionDefinition> AdditionsFor(PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage =>
        [
            new ContextActionDefinition(AddStageActionId, "Add stage", "mdi-layers-plus"),
            new ContextActionDefinition(AddJobActionId, "Add job", "mdi-plus-box-outline"),
            new ContextActionDefinition(AddDeploymentJobActionId, "Add deployment job", "mdi-rocket-launch-outline"),
        ],
        PipelineElementLocationKind.Job => [new ContextActionDefinition(AddStepActionId, "Add step", "mdi-plus-box-outline")],
        _ => [],
    };

    private static List<ContextActionDefinition> OrderingFor(PipelineElementLocation location)
    {
        if (location.Kind != PipelineElementLocationKind.Step || location.Job!.Steps.Any(step => step.IsFromTemplate))
        {
            return [];
        }

        var index = IndexOf(location);
        var actions = new List<ContextActionDefinition>();
        if (index > 0)
        {
            actions.Add(new ContextActionDefinition(MoveStepUpActionId, "Move up", "mdi-arrow-up", new ContextShortcutDefinition("ArrowUp", Alt: true)));
        }

        if (index < location.Job.Steps.Count - 1)
        {
            actions.Add(new ContextActionDefinition(MoveStepDownActionId, "Move down", "mdi-arrow-down", new ContextShortcutDefinition("ArrowDown", Alt: true)));
        }

        return actions;
    }

    private static bool Removable(PipelineModel model, PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage => model.Stages.Count(stage => !stage.IsImplicit) > 1,
        PipelineElementLocationKind.Job => location.Stage.Jobs.Count > 1,
        _ => location.Job!.Steps.Count > 1,
    };

    private static PipelineExecution Execution(PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage => location.Stage.Execution,
        PipelineElementLocationKind.Job => location.Job!.Execution,
        _ => location.Step!.Execution,
    };

    private static bool DependsOnDeclared(PipelineElementLocation location) =>
        location.Kind == PipelineElementLocationKind.Stage
            ? location.Stage.DependsOnDeclared
            : location.Job!.DependsOnDeclared;

    private static int IndexOf(PipelineElementLocation location) =>
        location.Job!.Steps.ToList().FindIndex(step => step.Id == location.Step!.Id);

    private static PipelineElementLocation? Resolve(DocumentEntry<PipelineBody, PipelineModel> entry, string elementId) =>
        entry.IsUsable && elementId.Length > 0 ? PipelineEdits.Locate(entry.Model, elementId) : null;

    private static PipelineOpener? OpenerOf(PipelineElementLocation location) => location switch
    {
        { Kind: PipelineElementLocationKind.Stage, Stage.Jobs.Count: > 0 } =>
            new PipelineOpener(location.Stage.Id, ToggleStageActionId, "Show jobs", "Hide jobs"),
        { Kind: PipelineElementLocationKind.Job } when location.Job!.Steps.Count > 0 =>
            new PipelineOpener(location.Job.Id, ToggleJobActionId, "Show steps", "Hide steps"),
        _ => null,
    };

    private sealed record PipelineOpener(string Id, string ActionId, string Show, string Hide);

    /// <summary>The property rows of <paramref name="elementId"/> (<c>PipelineContextPropertyProvider.Describe</c>).</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<PipelineBody, PipelineModel> entry, string elementId)
    {
        if (!entry.IsUsable || PipelineEdits.Locate(entry.Model, elementId) is not { } location)
        {
            return [];
        }

        var properties = location.Kind switch
        {
            PipelineElementLocationKind.Stage => StageProperties(entry.Model, location),
            PipelineElementLocationKind.Job => JobProperties(location),
            _ => StepProperties(location),
        };

        return location.Target.IsEditable
            ? properties
            : properties.Select(property => property with { ReadOnlyReason = location.Target.ReadOnlyReason }).ToList();
    }

    private static ContextPropertyDefinition DependsOnProperty(string label, IReadOnlyList<string> current, IReadOnlyList<string> candidates)
    {
        if (current.Count > 1)
        {
            return new ContextPropertyDefinition(
                DependsOnPropertyId,
                label,
                string.Join(", ", current),
                ContextPropertyEditor.Line,
                "This waits for several things, which is edited in the pipeline file.",
                OrderingGroup);
        }

        var choices = new List<string>(candidates) { NothingCandidate, DefaultCandidate };
        return new ContextPropertyDefinition(
            DependsOnPropertyId,
            label,
            current.Count == 1 ? current[0] : NothingCandidate,
            ContextPropertyEditor.Choice,
            candidates.Count == 0 ? "There is nothing before this for it to wait for." : "",
            OrderingGroup,
            choices);
    }

    private static List<ContextPropertyDefinition> StageProperties(PipelineModel model, PipelineElementLocation location)
    {
        var stage = location.Stage;
        var properties = new List<ContextPropertyDefinition>
        {
            ReadOnly("azure-pipeline.name", "Name", stage.Name, IdentityGroup),
            Editable(DisplayNamePropertyId, "Display name", stage.DisplayName, IdentityGroup),
            ReadOnly("azure-pipeline.job-count", "Jobs", stage.Jobs.Count.ToString(), IdentityGroup),
        };

        var graph = PipelineGraphBuilder.OfStages(model);
        var waitsFor = graph.Edges
            .Where(edge => edge.ToId == stage.Id)
            .Select(edge => edge.FromName)
            .ToList();

        var earlier = model.Stages
            .TakeWhile(candidate => !string.Equals(candidate.Id, stage.Id, StringComparison.Ordinal))
            .Where(candidate => candidate.Name.Length > 0)
            .Select(candidate => candidate.Name)
            .ToList();

        properties.Add(DependsOnProperty(
            stage.DependsOnDeclared ? "Depends on" : "Depends on (by default)",
            waitsFor,
            earlier));

        AddDeclared(properties, stage.Execution, PipelineExecution.ConditionKey, "azure-pipeline.condition", "Condition", stage.Execution.Condition, OrderingGroup);
        if (stage.TriggerIsManual)
        {
            properties.Add(ReadOnly("azure-pipeline.trigger", "Trigger", "manual", OrderingGroup));
        }

        AddIfPresent(properties, "azure-pipeline.is-skippable", "Skippable", stage.IsSkippable, OrderingGroup);
        AddPool(properties, stage.Pool);
        AddEnabled(properties, stage.Execution);
        return properties;
    }

    private static List<ContextPropertyDefinition> JobProperties(PipelineElementLocation location)
    {
        var job = location.Job!;
        var properties = new List<ContextPropertyDefinition>
        {
            ReadOnly("azure-pipeline.name", "Name", job.Name, IdentityGroup),
            Editable(DisplayNamePropertyId, "Display name", job.DisplayName, IdentityGroup),
            ReadOnly("azure-pipeline.kind", "Kind", job.IsDeployment ? "Deployment job" : "Job", IdentityGroup),
        };

        var siblings = location.Stage.Jobs
            .Where(candidate => !string.Equals(candidate.Id, job.Id, StringComparison.Ordinal) && candidate.Name.Length > 0)
            .Select(candidate => candidate.Name)
            .ToList();

        properties.Add(DependsOnProperty(
            job.DependsOnDeclared ? "Depends on" : "Depends on (by default)",
            job.DependsOn,
            siblings));

        AddDeclared(properties, job.Execution, PipelineExecution.ConditionKey, "azure-pipeline.condition", "Condition", job.Execution.Condition, OrderingGroup);
        AddPool(properties, job.Pool);
        AddIfPresent(properties, "azure-pipeline.environment", "Environment", job.Environment, ExecutionGroup);

        if (job.Strategy.Kind != PipelineStrategyKind.None)
        {
            properties.Add(ReadOnly("azure-pipeline.strategy", "Strategy", job.Strategy.Kind.ToString(), ExecutionGroup));
            if (job.Strategy.IsMultiplied)
            {
                properties.Add(ReadOnly(
                    "azure-pipeline.multiplicity",
                    "Runs",
                    job.Strategy.MultiplicityExpression.Length > 0
                        ? $"{job.Strategy.MultiplicityExpression} times - decided when the pipeline runs"
                        : $"{job.Strategy.Multiplicity} times",
                    ExecutionGroup));
            }
        }

        AddDeclared(properties, job.Execution, PipelineExecution.TimeoutKey, "azure-pipeline.timeout", "Timeout (minutes)", job.Execution.TimeoutInMinutes, ExecutionGroup);
        AddDeclared(properties, job.Execution, PipelineExecution.ContinueOnErrorKey, "azure-pipeline.continue-on-error", "Continue on error", job.Execution.ContinueOnError, ExecutionGroup);
        AddEnabled(properties, job.Execution);
        return properties;
    }

    private static List<ContextPropertyDefinition> StepProperties(PipelineElementLocation location)
    {
        var step = location.Step!;
        var properties = new List<ContextPropertyDefinition>
        {
            ReadOnly("azure-pipeline.kind", "Kind", step.Kind.ToString(), IdentityGroup),
            Editable(DisplayNamePropertyId, "Display name", step.DisplayName, IdentityGroup),
        };

        if (step.Identifier.Length > 0)
        {
            properties.Add(new ContextPropertyDefinition(
                "azure-pipeline.identifier",
                step.Kind.ToString(),
                step.Identifier,
                ContextPropertyEditor.Text,
                EditItInTheFile,
                IdentityGroup));
        }

        AddIfPresent(properties, "azure-pipeline.hook", "Lifecycle hook", step.Hook, IdentityGroup);
        AddDeclared(properties, step.Execution, PipelineExecution.ConditionKey, "azure-pipeline.condition", "Condition", step.Execution.Condition, OrderingGroup);
        AddDeclared(properties, step.Execution, PipelineExecution.TimeoutKey, "azure-pipeline.timeout", "Timeout (minutes)", step.Execution.TimeoutInMinutes, ExecutionGroup);
        AddDeclared(properties, step.Execution, PipelineExecution.ContinueOnErrorKey, "azure-pipeline.continue-on-error", "Continue on error", step.Execution.ContinueOnError, ExecutionGroup);
        AddEnabled(properties, step.Execution);
        return properties;
    }

    private static void AddPool(List<ContextPropertyDefinition> properties, PipelinePool pool)
    {
        if (!pool.IsDeclared)
        {
            return;
        }

        var reason = pool.Origin switch
        {
            PipelinePoolOrigin.Pipeline => "Set for the whole pipeline, at the top of the file.",
            PipelinePoolOrigin.Stage => "Set on the stage, not here.",
            _ => EditItInTheFile,
        };

        properties.Add(new ContextPropertyDefinition("azure-pipeline.pool", "Pool", pool.Label, ContextPropertyEditor.Line, reason, ExecutionGroup));
    }

    private static void AddEnabled(List<ContextPropertyDefinition> properties, PipelineExecution execution)
    {
        if (!execution.Has(PipelineExecution.EnabledKey))
        {
            return;
        }

        properties.Add(new ContextPropertyDefinition(
            EnabledPropertyId,
            "Enabled",
            execution.IsDisabled ? "false" : "true",
            ContextPropertyEditor.Toggle,
            IsExpression(execution.Enabled) ? "This is decided by an expression when the pipeline runs." : "",
            ExecutionGroup));
    }

    private static void AddDeclared(List<ContextPropertyDefinition> properties, PipelineExecution execution, string key, string id, string label, string value, string group)
    {
        if (execution.Has(key))
        {
            properties.Add(ReadOnly(id, label, value, group));
        }
    }

    private static void AddIfPresent(List<ContextPropertyDefinition> properties, string id, string label, string value, string group)
    {
        if (value.Length > 0)
        {
            properties.Add(ReadOnly(id, label, value, group));
        }
    }

    private static ContextPropertyDefinition ReadOnly(string id, string label, string value, string group) =>
        new(id, label, value, ContextPropertyEditor.Line, EditItInTheFile, group);

    private static ContextPropertyDefinition Editable(string id, string label, string value, string group) =>
        new(id, label, value, ContextPropertyEditor.Line, "", group);

    private static bool IsExpression(string value) =>
        value.Contains("${{", StringComparison.Ordinal) ||
        value.Contains("$(", StringComparison.Ordinal) ||
        value.Contains("$[", StringComparison.Ordinal);
}
