using System.Text;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// The parity corpus of the Azure DevOps pipeline (azure-devops-pipeline-disl-fbl Requirement 1.2): every
/// <c>.yml</c> under the test project's <c>Fixtures</c>, under the module's <c>examples</c> and under the shipped
/// copies in <c>src/examples/diagrams/azure-devops-pipeline</c>, each with the folder it is read in.
/// </summary>
/// <remarks>
/// <para>
/// <b>A document is read in its own folder</b>: the fixtures in <c>Fixtures</c>, an example in its example's
/// folder. That is the workspace its templates are followed in, so <c>templates.yml</c> reads its templates and a
/// template under <c>templates/</c> is read as a pipeline of its own as well.
/// </para>
/// <para>
/// <b>The order</b> is the fixtures, then the module's examples, then the shipped copies, each by path.
/// </para>
/// </remarks>
internal static class PipelineCorpus
{
    /// <summary>This test project's name.</summary>
    public const string TestProject = "EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests";

    /// <summary>The module's folder, <c>src/diagrams/azure-devops-pipeline</c>.</summary>
    public static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "azure-devops-pipeline");

    /// <summary>The repository's <c>src</c> folder, which every corpus path is relative to.</summary>
    public static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The corpus, in the order it is recorded.</summary>
    public static IReadOnlyList<Document> Documents()
    {
        var fixtures = Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures");
        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples"));
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "azure-devops-pipeline"));
        return [.. In(fixtures), .. examples.Order(StringComparer.Ordinal).SelectMany(In), .. shipped.Order(StringComparer.Ordinal).SelectMany(In)];

        static IEnumerable<Document> In(string root) =>
            Directory.GetFiles(root, "*.yml", SearchOption.AllDirectories)
                .Select(path => new Document(UnderSource(path), UnderSource(root), Path.GetRelativePath(root, path).Replace('\\', '/')))
                .OrderBy(document => document.Relative, StringComparer.Ordinal);
    }

    /// <summary>
    /// A digest of every <c>.yml</c> in the document's folder, by path and bytes: two copies of a document are
    /// the same reading only where the templates they can follow are the same too.
    /// </summary>
    public static async Task<string> FolderShaAsync(Document document, CancellationToken cancellationToken)
    {
        var root = Path.Combine(SourceFolder, document.Root);
        var digest = new StringBuilder();
        foreach (var path in Directory.GetFiles(root, "*.yml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            digest.Append(Path.GetRelativePath(root, path).Replace('\\', '/')).Append(' ')
                .Append(TranscriptText.Sha256(await File.ReadAllBytesAsync(path, cancellationToken))).Append('\n');
        }

        return TranscriptText.Sha256(Encoding.UTF8.GetBytes(digest.ToString()));
    }

    /// <summary>Copies every file under <paramref name="from"/> to <paramref name="to"/>, keeping the folders.</summary>
    public static void CopyFolder(string from, string to)
    {
        foreach (var path in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(to, Path.GetRelativePath(from, path));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(path, copy);
        }
    }

    private static string UnderSource(string path) => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/');

    /// <summary>One document of the corpus.</summary>
    /// <param name="Relative">Its path relative to <c>src/</c>, with forward slashes.</param>
    /// <param name="Root">The folder it is read in, relative to <c>src/</c>.</param>
    /// <param name="InFolder">Its path within that folder.</param>
    public sealed record Document(string Relative, string Root, string InFolder);
}
