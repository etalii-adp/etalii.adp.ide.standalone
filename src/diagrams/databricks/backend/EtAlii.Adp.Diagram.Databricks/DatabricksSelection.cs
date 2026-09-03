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

    /// <summary>The task an id names, with the job declaring it; null when it names none.</summary>
    public static (JobModel Job, JobTask Task)? TaskOf(DatabricksDocumentEntry entry, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("task:", StringComparison.Ordinal))
        {
            return null;
        }

        var key = elementId["task:".Length..];
        foreach (var job in entry.Jobs)
        {
            if (job.Tasks.FirstOrDefault(task => task.Key == key) is { } task)
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
        DatabricksDocumentEntry entry, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("library:", StringComparison.Ordinal))
        {
            return null;
        }

        var path = elementId["library:".Length..];
        foreach (var pipeline in entry.Pipelines)
        {
            if (pipeline.Libraries.FirstOrDefault(library => library.Path == path) is { } library)
            {
                return (pipeline, library);
            }
        }

        return null;
    }

    /// <summary>The target frame an id names.</summary>
    public static BundleTarget? TargetOf(DatabricksDocumentEntry entry, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("target:", StringComparison.Ordinal))
        {
            return null;
        }

        var name = elementId["target:".Length..];
        return entry.Bundle.Targets.FirstOrDefault(target => target.Name == name);
    }

    /// <summary>
    /// The display text an id resolves to, or null when the entry holds nothing by that id -
    /// which is what tells the resolver another module's element is being asked about.
    /// </summary>
    public static string? Describe(DatabricksDocumentEntry entry, string elementId)
    {
        if (TaskOf(entry, elementId) is { } selected)
        {
            return selected.Task.Key;
        }

        if (TryEdge(elementId, out var from, out var to))
        {
            return entry.Jobs.SelectMany(job => job.Tasks).Any(task =>
                task.Key == to && task.DependsOn.Any(dependency => dependency.TaskKey == from))
                ? $"{from} → {to}"
                : null;
        }

        if (IsBundle(elementId))
        {
            return entry.Bundle.Name.Length > 0 ? entry.Bundle.Name : "bundle";
        }

        if (IsPipelineNode(elementId))
        {
            var pipeline = entry.Pipelines.FirstOrDefault();
            return pipeline is null ? null : pipeline.Name.Length > 0 ? pipeline.Name : "pipeline";
        }

        if (LibraryOf(entry, elementId) is { } library)
        {
            return library.Library.Path;
        }

        if (TargetOf(entry, elementId) is { } target)
        {
            return target.Name;
        }

        if (elementId.StartsWith("cluster:", StringComparison.Ordinal))
        {
            var key = elementId["cluster:".Length..];
            return entry.Jobs.SelectMany(job => job.Clusters).Any(cluster => cluster.Key == key) ? key : null;
        }

        if (elementId.StartsWith("resource:", StringComparison.Ordinal))
        {
            var pair = elementId["resource:".Length..];
            return entry.Bundle.Resources.Any(resource => $"{resource.Kind}/{resource.Key}" == pair)
                ? pair[(pair.IndexOf('/') + 1)..]
                : null;
        }

        return null;
    }
}
