using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The job diagram's computed layout: longest-path layering left-to-right over the
/// <c>depends_on</c> DAG, cycle-tolerant - a back-edge is excluded from layering and still
/// drawn (databricks-diagrams Requirement 4.6). A pure function; the layout overlay merges
/// authored positions on top, element by element.
/// </summary>
internal static class DatabricksJobLayout
{
    private const double ColumnWidth = 260;
    private const double RowHeight = 120;
    private const double ClusterBandGap = 80;

    /// <summary>
    /// A position for every task, every unresolved <c>depends_on</c> stub, and every cluster.
    /// Ids as the mapper speaks them: <c>task:</c>, <c>cluster:</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Positions(JobModel job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var layers = Layers(job, out var stubs);

        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        var rows = new Dictionary<int, int>();
        var deepest = 0;
        foreach (var task in job.Tasks.Where(task => task.Key.Length > 0))
        {
            var layer = layers[task.Key];
            rows.TryGetValue(layer, out var row);
            rows[layer] = row + 1;
            deepest = Math.Max(deepest, row);
            positions[$"task:{task.Key}"] = new RegistrationPosition(layer * ColumnWidth, row * RowHeight);
        }

        // A stub sits one column left of the first task that names it, in its own row beneath
        // the layer - visible, marked missing, and out of the healthy tasks' way.
        foreach ((string stubKey, int dependentLayer) in stubs)
        {
            var layer = Math.Max(dependentLayer - 1, 0);
            rows.TryGetValue(layer, out var row);
            rows[layer] = row + 1;
            deepest = Math.Max(deepest, row);
            positions[$"task:{stubKey}"] = new RegistrationPosition(layer * ColumnWidth, row * RowHeight);
        }

        // Clusters in a band beneath the DAG, in declaration order.
        var clusterY = (deepest + 1) * RowHeight + ClusterBandGap;
        for (var index = 0; index < job.Clusters.Count; index++)
        {
            positions[$"cluster:{job.Clusters[index].Key}"] =
                new RegistrationPosition(index * ColumnWidth, clusterY);
        }

        return positions;
    }

    /// <summary>
    /// Each task's layer: the longest dependency path leading to it, with edges that close a
    /// circle contributing nothing - so a cycle neither hangs the walk nor drags its members
    /// rightward forever, and its tasks still land somewhere drawable.
    /// </summary>
    private static Dictionary<string, int> Layers(
        JobModel job, out List<(string StubKey, int DependentLayer)> stubs)
    {
        var byKey = new Dictionary<string, JobTask>(StringComparer.Ordinal);
        foreach (var task in job.Tasks.Where(task => task.Key.Length > 0))
        {
            byKey.TryAdd(task.Key, task);
        }

        var layers = new Dictionary<string, int>(StringComparer.Ordinal);
        var walking = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in byKey.Keys)
        {
            LayerOf(key);
        }

        // The stubs: keys depends_on names and no task declares (Requirement 4.5), each with
        // the layer of the first task depending on it.
        var found = new List<(string, int)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in job.Tasks.Where(task => task.Key.Length > 0))
        {
            foreach (var dependency in task.DependsOn)
            {
                if (!byKey.ContainsKey(dependency.TaskKey) && seen.Add(dependency.TaskKey))
                {
                    found.Add((dependency.TaskKey, layers[task.Key]));
                }
            }
        }

        stubs = found;
        return layers;

        int LayerOf(string key)
        {
            if (layers.TryGetValue(key, out var known))
            {
                return known;
            }

            if (!walking.Add(key))
            {
                // A back-edge: this task is already on the walk below us. Excluded from
                // layering; the edge itself is still drawn by the mapper.
                return -1;
            }

            var layer = 0;
            foreach (var dependency in byKey[key].DependsOn)
            {
                if (byKey.ContainsKey(dependency.TaskKey))
                {
                    layer = Math.Max(layer, LayerOf(dependency.TaskKey) + 1);
                }
            }

            walking.Remove(key);
            layers[key] = layer;
            return layer;
        }
    }
}
