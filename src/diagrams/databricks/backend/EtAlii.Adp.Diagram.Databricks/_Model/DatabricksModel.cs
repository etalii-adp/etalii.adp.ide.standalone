using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// What one Databricks configuration file parsed to under each of the family's readings.
/// </summary>
/// <remarks>
/// All three readings are carried at once because one file legitimately serves several: a
/// <c>databricks.yml</c> is a bundle that may also declare jobs and pipelines inline, and a
/// resource file holds jobs the bundle diagram counts and the job diagram opens. Each parser
/// simply finds nothing where its structures are absent, so the empty readings cost one pass
/// each over an already-loaded tree.
/// </remarks>
/// <param name="Bundle">The file read as a bundle; <see cref="BundleModel.Empty"/> where it is not one.</param>
/// <param name="Jobs">Every job the file declares, in file order.</param>
/// <param name="Pipelines">Every pipeline the file declares - or the one a bare settings file is.</param>
public sealed record DatabricksModel(
    BundleModel Bundle,
    IReadOnlyList<JobModel> Jobs,
    IReadOnlyList<PipelineModel> Pipelines)
{
    /// <summary>What a file that does not parse reads as: nothing under any reading.</summary>
    public static DatabricksModel Empty { get; } = new(BundleModel.Empty, [], []);

    /// <summary>The three readings of <paramref name="document"/>.</summary>
    /// <exception cref="YamlDotNet.Core.YamlException">The document is not YAML (or JSON, the same door).</exception>
    public static DatabricksModel Read(LineDocument document)
    {
        var root = YamlAccess.Root(document);
        return new DatabricksModel(
            BundleParser.Parse(root, document),
            JobParser.Parse(root, document),
            PipelineParser.Parse(root, document));
    }
}
