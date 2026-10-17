using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>The fixture pipelines the module's tests sweep.</summary>
internal static class PipelineFixtures
{
    /// <summary>
    /// Every <c>.yml</c> under <paramref name="folder"/>, templates included, except the documents under
    /// <c>findings/</c>.
    /// </summary>
    /// <remarks>
    /// A document under <c>findings/</c> exists to show one finding of azure-devops-pipeline-disl-fbl - one that
    /// does not parse, two stages of one name - and is judged by the parity transcript, which records what each
    /// answers, a throw included. A sweep over real pipelines that asserted it parses or draws would fail on
    /// the finding it was written to show.
    /// </remarks>
    public static string[] Pipelines(string folder) =>
    [
        .. Directory.GetFiles(folder, "*.yml", SearchOption.AllDirectories)
            .Where(path => !IoPath.GetRelativePath(folder, path).Replace('\\', '/').StartsWith("findings/", StringComparison.Ordinal)),
    ];
}
