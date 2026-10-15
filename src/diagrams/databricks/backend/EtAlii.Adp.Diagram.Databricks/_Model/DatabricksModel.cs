using EtAlii.Adp.Documents;
using Serilog;
using YamlDotNet.Core;

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
    private static readonly ILogger _logger = Log.ForContext<DatabricksModel>();

    /// <summary>What a file that does not parse reads as: nothing under any reading.</summary>
    private static DatabricksModel Empty { get; } = new(BundleModel.Empty, [], []);

    /// <summary>The three readings of <paramref name="document"/>.</summary>
    /// <exception cref="YamlException">The document is not YAML (or JSON, the same door).</exception>
    public static DatabricksModel Read(LineDocument document)
    {
        var root = YamlAccess.Root(document);
        return new DatabricksModel(
            BundleParser.Parse(root, document),
            JobParser.Parse(root, document),
            PipelineParser.Parse(root, document));
    }

    /// <summary>
    /// What the store and the validator keep of <paramref name="document"/>: its three readings, or,
    /// for a text that is not YAML, the empty model, the reader's sentence on the line it names, and
    /// that sentence as the reason nothing is written to it. <b>Never throws.</b>
    /// </summary>
    public static ParseOutcome<DatabricksModel> Outcome(LineDocument document)
    {
        try
        {
            return new ParseOutcome<DatabricksModel>(Read(document), []);
        }
        catch (YamlException exception)
        {
            _logger.Debug(exception, "A Databricks file does not parse at line {Line}", exception.Start.Line);
            var line = (int)Math.Max(exception.Start.Line, 1) - 1;
            return new ParseOutcome<DatabricksModel>(Empty, [new ParseProblem(line, exception.Message)], exception.Message);
        }
    }
}
