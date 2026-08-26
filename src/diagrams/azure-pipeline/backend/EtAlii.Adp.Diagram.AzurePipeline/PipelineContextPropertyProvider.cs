using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// What a selected pipeline element shows in the Property Grid, and which of it may be changed.
/// </summary>
/// <remarks>
/// <para>
/// Almost everything here is <b>shown but not editable</b>, and that is the point rather than an
/// omission. A pipeline file is executable configuration: a pool, a strategy, an environment or a
/// condition is edited better in a text editor, and a bad write breaks a build. So the panel
/// answers "what decides whether this runs" - which is the question a reader has - while only the
/// three properties Requirement 9.1 permits can be written from here.
/// </para>
/// <para>
/// A read-only property carries a <b>reason</b>, not a flag. The value is real and worth seeing,
/// and the reader's next question is where it is written; a greyed-out box answers the first and
/// not the second. For an element that came from a template the reason names the file, which is
/// the case that makes the difference obvious (Requirement 13.7).
/// </para>
/// <para>
/// A property that is <b>absent</b> from the document is not contributed at all. A stage with no
/// <c>condition</c> has no condition row; a stage with <c>condition: ''</c> has a row whose value
/// is empty. Those are different states of the file, and a sentinel value would flatten them into
/// one (Requirement 13.10).
/// </para>
/// </remarks>
public sealed class PipelineContextPropertyProvider : IContextPropertyProvider
{
    /// <summary>The element's <c>displayName</c> - one of the three editable properties.</summary>
    public const string DisplayNamePropertyId = "azure-pipeline.display-name";

    /// <summary>What the element waits for. Editable, and validated before it is written.</summary>
    public const string DependsOnPropertyId = "azure-pipeline.depends-on";

    /// <summary>Whether the element runs at all.</summary>
    public const string EnabledPropertyId = "azure-pipeline.enabled";

    /// <summary>The heading identity properties sort under.</summary>
    public const string IdentityGroup = "Identity";

    /// <summary>The heading for what decides when this runs relative to everything else.</summary>
    public const string OrderingGroup = "Ordering";

    /// <summary>The heading for what decides how it runs once it does.</summary>
    public const string ExecutionGroup = "Execution";

    /// <summary>
    /// Said of every property a text editor edits better - which is all of them but three.
    /// </summary>
    private const string EditItInTheFile = "This is edited in the pipeline file. A wrong value here would break the build.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IPipelineDocumentStore _documents;

    /// <summary>Creates the provider.</summary>
    public PipelineContextPropertyProvider(IHistoryStackStore historyStacks, IPipelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (Resolve(target) is not { } found)
        {
            return None();
        }

        var (model, location) = found;
        var properties = location.Kind switch
        {
            PipelineElementLocationKind.Stage => StageProperties(model, location),
            PipelineElementLocationKind.Job => JobProperties(location),
            _ => StepProperties(location),
        };

        // An element whose text lives in a template is worth showing and cannot be written here,
        // whatever the rules above would otherwise permit (Requirement 13.7).
        return location.Target.IsEditable
            ? Ok(properties)
            : Ok(properties.Select(property => property with { ReadOnlyReason = location.Target.ReadOnlyReason }).ToList());
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (Resolve(target) is not { } found)
        {
            return ContextPropertyResult.Failure("That element is no longer in this pipeline.");
        }

        var (_, location) = found;
        var command = CommandFor(target, propertyId, value, location);
        if (command is null)
        {
            return ContextPropertyResult.Failure($"'{propertyId}' cannot be set on this element.");
        }

        // The same command the canvas and the context menu use, on the project's history - so a
        // rename from the panel and a rename from the diagram are one implementation and one undo
        // (Requirement 13.11).
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private static ICommand? CommandFor(ContextTarget target, string propertyId, string value, PipelineElementLocation location)
    {
        var root = target.RootPath;
        var body = target.ResolvedFullPath;
        var id = location.Id;

        return propertyId switch
        {
            DisplayNamePropertyId => new RenamePipelineElementCommand(root, body, id, value.Trim()),
            EnabledPropertyId => new SetPipelineElementEnabledCommand(root, body, id, IsTrue(value)),
            DependsOnPropertyId when location.Kind != PipelineElementLocationKind.Step =>
                DependenciesCommand(root, body, id, value),
            _ => null,
        };
    }

    /// <summary>
    /// A <c>dependsOn</c> typed as text.
    /// </summary>
    /// <remarks>
    /// Comma-separated, because a single <c>Value</c> string cannot carry a list without some
    /// convention and this is the one a user would guess. It is a stopgap: Requirement 13.14 owes
    /// this property a Choice editor with the candidate names, and until that exists a text box
    /// gives the user no idea what the valid names are. Nothing is unsafe about it - an unknown
    /// name is refused with a reason and the document is left alone - it is merely worse.
    /// <para>
    /// Empty text means "wait for nothing", written as <c>dependsOn: []</c>. Clearing the property
    /// back to the schema's default is a separate action, because those are different instructions
    /// and a text box cannot express both.
    /// </para>
    /// </remarks>
    private static ICommand DependenciesCommand(string root, string body, string id, string value)
    {
        var names = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new SetPipelineDependenciesCommand(root, body, id, names, Declared: true);
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

        // What it waits for, resolved rather than absent: a stage relying on the sequential
        // default does wait for something, and showing an empty row would say it did not
        // (Requirement 13.2).
        var graph = PipelineGraphBuilder.OfStages(model);
        var waitsFor = graph.Edges
            .Where(edge => edge.ToId == stage.Id)
            .Select(edge => edge.FromName)
            .ToList();

        properties.Add(new ContextPropertyDefinition(
            DependsOnPropertyId,
            stage.DependsOnDeclared ? "Depends on" : "Depends on (by default)",
            string.Join(", ", waitsFor),
            ContextPropertyEditor.Line,
            "",
            OrderingGroup));

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

        // A job's default is to wait for nothing, so unlike a stage there is nothing implicit to
        // resolve - what the file says is the whole of it.
        if (job.DependsOnDeclared || job.DependsOn.Count > 0)
        {
            properties.Add(Editable(DependsOnPropertyId, "Depends on", string.Join(", ", job.DependsOn), OrderingGroup));
        }
        else
        {
            properties.Add(new ContextPropertyDefinition(
                DependsOnPropertyId,
                "Depends on (by default)",
                "nothing - it runs as soon as its stage does",
                ContextPropertyEditor.Line,
                "",
                OrderingGroup));
        }

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

        // The value that identifies it - a task's name, a script's text. Shown as several lines
        // because a script routinely is several, and a one-line box would hide most of it.
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

    /// <summary>
    /// Where it runs, and - when it was not set here - where that was decided.
    /// </summary>
    /// <remarks>
    /// The origin is in the reason rather than in the value, because "ubuntu-latest" is the answer
    /// to "where does this run" and "the pipeline sets it" is the answer to the reader's next
    /// question, which is where to go and change it.
    /// </remarks>
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

        properties.Add(new ContextPropertyDefinition(
            "azure-pipeline.pool",
            "Pool",
            pool.Label,
            ContextPropertyEditor.Line,
            reason,
            ExecutionGroup));
    }

    /// <summary>
    /// Whether it runs. Contributed only when the file says so, per Requirement 13.10 - but
    /// editable, so switching something off is one of the three things the panel can do.
    /// </summary>
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
            // An expression decides this at run time, so there is no boolean here to flip.
            IsExpression(execution.Enabled) ? "This is decided by an expression when the pipeline runs." : "",
            ExecutionGroup));
    }

    /// <summary>
    /// Adds a row when the document declares the key, whatever its value (Requirement 13.10).
    /// </summary>
    /// <remarks>
    /// Presence, not emptiness. A stage with <c>condition: ''</c> gets a row whose value is empty,
    /// because that is a thing the file says; a stage with no <c>condition</c> gets no row at all.
    /// Testing the string instead would collapse the two, which is exactly what 13.10 forbids.
    /// </remarks>
    private static void AddDeclared(
        List<ContextPropertyDefinition> properties,
        PipelineExecution execution,
        string key,
        string id,
        string label,
        string value,
        string group)
    {
        if (execution.Has(key))
        {
            properties.Add(ReadOnly(id, label, value, group));
        }
    }

    /// <summary>
    /// Adds a row for a value that has no declared/empty distinction to make - a pool name, an
    /// environment - where an empty string means there is nothing to show.
    /// </summary>
    private static void AddIfPresent(
        List<ContextPropertyDefinition> properties,
        string id,
        string label,
        string value,
        string group)
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

    private static bool IsTrue(string value) => string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a value is decided at compile or run time rather than written out.</summary>
    private static bool IsExpression(string value) =>
        value.Contains("${{", StringComparison.Ordinal) ||
        value.Contains("$(", StringComparison.Ordinal) ||
        value.Contains("$[", StringComparison.Ordinal);

    private (PipelineModel Model, PipelineElementLocation Location)? Resolve(ContextTarget target)
    {
        if (target.Scope != ContextScope.DiagramElement || target.ElementId.Length == 0)
        {
            return null;
        }

        var entry = _documents.GetOrLoad(target.RootPath, target.ResolvedFullPath);
        if (!entry.IsUsable)
        {
            return null;
        }

        var location = PipelineEdits.Locate(entry.Model, target.ElementId);
        return location is null ? null : (entry.Model, location);
    }

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> None() =>
        ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Ok(IReadOnlyList<ContextPropertyDefinition> properties) =>
        ValueTask.FromResult(properties);
}
