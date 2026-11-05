using EtAlii.Adp.Context;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// What one of this family's element ids names inside a loaded entry - the shared vocabulary
/// the resolver, the action provider and the property provider all answer from, so the three
/// can never disagree about what is selected.
/// </summary>
internal static class DatabricksSelection
{
    /// <summary>
    /// Whether a path could be one of this family's bodies at all. The extensions are shared
    /// (that is the family's whole routing story), so this is a cheap first gate, not a claim.
    /// </summary>
    public static bool CouldBeFamilyFile(string path) =>
        IoPath.GetExtension(path).ToLowerInvariant() is ".yml" or ".yaml" or ".json";

    /// <summary>
    /// What of <paramref name="model"/> the registration <paramref name="target"/> was resolved through
    /// shows (finding B3): under a job registration only the job its <c>resource:</c> header names, or
    /// the file's first without one, and likewise for a pipeline - the one <see cref="DatabricksSession"/>
    /// draws - so menus, rows, drops and commands act on that resource rather than the file's first.
    /// </summary>
    /// <remarks>
    /// A target with no registration, and a bundle's, sees the whole file, as every target did before
    /// targets carried their registration. A header that names nothing leaves nothing to act on, as the
    /// session draws nothing for it.
    /// </remarks>
    public static DatabricksModel ReadingOf(DatabricksModel model, ContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(target);

        if (target.RegistrationPath is not { Length: > 0 } registration)
        {
            return model;
        }

        var key = DatabricksBody.ResourceOf(registration);
        if (target.Origin == ServiceCollectionAddDatabricksExtension.JobOrigin)
        {
            return model with { Jobs = Named(model.Jobs, job => job.Key, key) };
        }

        return target.Origin == ServiceCollectionAddDatabricksExtension.PipelineOrigin
            ? model with { Pipelines = Named(model.Pipelines, pipeline => pipeline.Key, key) }
            : model;

        static IReadOnlyList<T> Named<T>(IReadOnlyList<T> candidates, Func<T, string> keyOf, string? named) =>
            named is { Length: > 0 }
                ? [.. candidates.Where(candidate => keyOf(candidate) == named).Take(1)]
                : [.. candidates.Take(1)];
    }

    /// <summary>
    /// The resource key the <c>resource:</c> header of <paramref name="target"/>'s registration names, or
    /// empty - the file's first - without one: what a command names when the reading holds no resource.
    /// </summary>
    public static string ResourceKeyOf(ContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return DatabricksBody.ResourceOf(target.RegistrationPath) ?? "";
    }

    /// <summary>The task an id names, with the job declaring it; null when it names none.</summary>
    public static (JobModel Job, JobTask Task)? TaskOf(DatabricksModel model, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("task:", StringComparison.Ordinal))
        {
            return null;
        }

        var key = elementId["task:".Length..];
        foreach (var job in model.Jobs)
        {
            if (job.Tasks.FirstOrDefault(t => t.Key == key) is { } task)
            {
                return (job, task);
            }
        }

        return null;
    }

    /// <summary>The dependency edge an id names: <c>edge:{from}-&gt;{to}</c>.</summary>
    public static bool TryEdge(string? elementId, out string fromKey, out string toKey)
    {
        fromKey = "";
        toKey = "";
        if (elementId is null || !elementId.StartsWith("edge:", StringComparison.Ordinal))
        {
            return false;
        }

        var payload = elementId["edge:".Length..];
        var separator = payload.IndexOf("->", StringComparison.Ordinal);
        if (separator <= 0 || separator >= payload.Length - 2)
        {
            return false;
        }

        fromKey = payload[..separator];
        toKey = payload[(separator + 2)..];
        return true;
    }

    /// <summary>Whether the id is the bundle node itself.</summary>
    public static bool IsBundle(string? elementId) => elementId == "bundle";

    /// <summary>Whether the id is the pipeline node itself.</summary>
    public static bool IsPipelineNode(string? elementId) => elementId == "pipeline";

    /// <summary>The library an id names, with the pipeline declaring it.</summary>
    public static (PipelineModel Pipeline, PipelineLibrary Library)? LibraryOf(
        DatabricksModel model, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("library:", StringComparison.Ordinal))
        {
            return null;
        }

        var path = elementId["library:".Length..];
        foreach (var pipeline in model.Pipelines)
        {
            if (pipeline.Libraries.FirstOrDefault(l => l.Path == path) is { } library)
            {
                return (pipeline, library);
            }
        }

        return null;
    }

    /// <summary>The target frame an id names.</summary>
    public static BundleTarget? TargetOf(DatabricksModel model, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("target:", StringComparison.Ordinal))
        {
            return null;
        }

        var name = elementId["target:".Length..];
        return model.Bundle.Targets.FirstOrDefault(target => target.Name == name);
    }

    /// <summary>
    /// The display text an id resolves to, or null when the entry holds nothing by that id -
    /// which is what tells the resolver another module's element is being asked about.
    /// </summary>
    public static string? Describe(DatabricksModel model, string elementId)
    {
        if (TaskOf(model, elementId) is { } selected)
        {
            return selected.Task.Key;
        }

        if (TryEdge(elementId, out var from, out var to))
        {
            return model.Jobs.SelectMany(job => job.Tasks).Any(task =>
                task.Key == to && task.DependsOn.Any(dependency => dependency.TaskKey == from))
                ? $"{from} → {to}"
                : null;
        }

        if (IsBundle(elementId))
        {
            return model.Bundle.Name.Length > 0 ? model.Bundle.Name : "bundle";
        }

        if (IsPipelineNode(elementId))
        {
            var pipeline = model.Pipelines.FirstOrDefault();
            return pipeline is null ? null : pipeline.Name.Length > 0 ? pipeline.Name : "pipeline";
        }

        if (LibraryOf(model, elementId) is { } library)
        {
            return library.Library.Path;
        }

        if (TargetOf(model, elementId) is { } target)
        {
            return target.Name;
        }

        if (OverrideEdgeOf(model, elementId) is { } overrideEdge)
        {
            return overrideEdge;
        }

        if (FlowEdgeOf(model, elementId) is { } flowEdge)
        {
            return flowEdge;
        }

        if (elementId.StartsWith("cluster:", StringComparison.Ordinal))
        {
            var key = elementId["cluster:".Length..];
            return model.Jobs.SelectMany(job => job.Clusters).Any(cluster => cluster.Key == key) ? key : null;
        }

        if (elementId.StartsWith("resource:", StringComparison.Ordinal))
        {
            var pair = elementId["resource:".Length..];
            return model.Bundle.Resources.Any(resource => $"{resource.Kind}/{resource.Key}" == pair)
                ? pair[(pair.IndexOf('/') + 1)..]
                : null;
        }

        return null;
    }
    /// <summary>
    /// A bundle's override edge, <c>override:{target}/{kind}/{key}</c>, as "target → kind/key" - or null
    /// unless that target really overrides that resource. Drawn by the bundle reading but named by nothing
    /// here until centralized-selection task 26, so no override edge could be selected.
    /// </summary>
    private static string? OverrideEdgeOf(DatabricksModel model, string elementId)
    {
        if (!elementId.StartsWith("override:", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = elementId["override:".Length..];
        var targetEnd = rest.IndexOf('/', StringComparison.Ordinal);
        if (targetEnd <= 0)
        {
            return null;
        }

        var targetName = rest[..targetEnd];
        var resource = rest[(targetEnd + 1)..];
        var target = model.Bundle.Targets.FirstOrDefault(candidate => candidate.Name == targetName);
        return target is not null && target.Overrides.Any(overridden => $"{overridden.Kind}/{overridden.Key}" == resource)
            ? $"{targetName} → {resource}"
            : null;
    }

    /// <summary>
    /// A pipeline reading's flow edge - <c>flow:{library}->pipeline</c> or <c>flow:pipeline->target</c> -
    /// as "from → to", or null unless the pipeline really has that library (or exists at all, for the
    /// target edge). Named by nothing here until centralized-selection task 26.
    /// </summary>
    private static string? FlowEdgeOf(DatabricksModel model, string elementId)
    {
        if (!elementId.StartsWith("flow:", StringComparison.Ordinal))
        {
            return null;
        }

        var pipeline = model.Pipelines.FirstOrDefault();
        if (pipeline is null)
        {
            return null;
        }

        var name = pipeline.Name.Length > 0 ? pipeline.Name : "pipeline";
        if (elementId == "flow:pipeline->target")
        {
            return $"{name} → target";
        }

        const string intoPipeline = "->pipeline";
        if (!elementId.EndsWith(intoPipeline, StringComparison.Ordinal))
        {
            return null;
        }

        var library = elementId["flow:".Length..^intoPipeline.Length];
        return pipeline.Libraries.Any(candidate => candidate.Path == library) ? $"{library} → {name}" : null;
    }
}
