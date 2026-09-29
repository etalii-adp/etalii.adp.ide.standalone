namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// The YAML a newly added stage, job or step is written as.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from the writer because these are two different jobs: the writer knows where lines
/// go, this knows what they say. It also means the text can be asserted on directly, which for
/// something that writes into an executable build definition is worth being able to do.
/// </para>
/// <para>
/// Every block is the smallest thing Azure DevOps will actually run. A stage with no jobs and a
/// job with no steps are both schema errors, so adding one would hand the user a pipeline that
/// fails at queue time - the diagram would have "worked" and the build would not.
/// </para>
/// </remarks>
public static class PipelineBlocks
{
    /// <summary>The placeholder step a new job gets, so the job is runnable the moment it exists.</summary>
    public const string PlaceholderScript = "echo Add your build steps here";

    /// <summary>A stage, with one job and one step inside it.</summary>
    /// <param name="name">Its <c>stage:</c> value.</param>
    /// <param name="indent">The column the <c>-</c> of a stage entry sits at.</param>
    public static IReadOnlyList<string> Stage(string name, int indent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var pad = new string(' ', indent);
        var inner = new string(' ', indent + 2);
        return
        [
            $"{pad}- stage: {name}",
            $"{inner}jobs:",
            .. Job($"{name}Job", indent + 4),
        ];
    }

    /// <summary>A job, with one step inside it.</summary>
    /// <param name="name">Its <c>job:</c> value.</param>
    /// <param name="indent">The column the <c>-</c> of a job entry sits at.</param>
    public static IReadOnlyList<string> Job(string name, int indent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var pad = new string(' ', indent);
        var inner = new string(' ', indent + 2);
        return
        [
            $"{pad}- job: {name}",
            $"{inner}steps:",
            .. Step(PlaceholderScript, indent + 4),
        ];
    }

    /// <summary>A script step.</summary>
    /// <param name="script">What it runs.</param>
    /// <param name="indent">The column the <c>-</c> of a step entry sits at.</param>
    public static IReadOnlyList<string> Step(string script, int indent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        var pad = new string(' ', indent);
        return [$"{pad}- script: {script}"];
    }

    /// <summary>A deployment job, with the environment and strategy the schema requires of one.</summary>
    /// <remarks>
    /// A <c>deployment</c> without an <c>environment</c> and a <c>strategy</c> does not run, so
    /// both are written. The environment name is the job's, which is a guess - but a named
    /// environment the user renames is a better starting point than a blank one they must fill in
    /// before anything works.
    /// </remarks>
    /// <param name="name">Its <c>deployment:</c> value.</param>
    /// <param name="environment">The environment it targets.</param>
    /// <param name="indent">The column the <c>-</c> of a job entry sits at.</param>
    public static IReadOnlyList<string> DeploymentJob(string name, string environment, int indent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);

        var pad = new string(' ', indent);
        var inner = new string(' ', indent + 2);
        return
        [
            $"{pad}- deployment: {name}",
            $"{inner}environment: {environment}",
            $"{inner}strategy:",
            $"{inner}  runOnce:",
            $"{inner}    deploy:",
            $"{inner}      steps:",
            .. Step(PlaceholderScript, indent + 8),
        ];
    }

    /// <summary>
    /// A name nothing in <paramref name="taken"/> already uses, starting from <paramref name="wanted"/>.
    /// </summary>
    /// <remarks>
    /// Azure matches <c>dependsOn</c> by name, so two stages sharing one is not a cosmetic clash -
    /// it makes the pipeline's ordering ambiguous. A new element therefore gets a name that is
    /// free, rather than one that collides and has to be noticed later.
    /// </remarks>
    public static string UnusedName(string wanted, IEnumerable<string> taken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wanted);
        ArgumentNullException.ThrowIfNull(taken);

        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(wanted))
        {
            return wanted;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{wanted}{suffix}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
