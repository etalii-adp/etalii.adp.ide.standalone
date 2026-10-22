using System.Text.Json;
using EtAlii.Adp.Diagram;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// What one module tells <see cref="TranscriptHarness"/>: where its transcript lives, its toolbox,
/// its corpus, and what it records for one document. Everything else - the header, the documents
/// array, a document recorded once and then as <c>sameAs</c>, the scratch folder, the closing line
/// ending - is the harness's, the same for every module.
/// </summary>
/// <remarks>
/// <b>The adapter is named for its file.</b> The transcript's <c>generator</c> field names
/// <c>Parity/&lt;adapter type&gt;.cs</c>, so a module's adapter keeps the name its generator always
/// had and the field stays as recorded.
/// </remarks>
public abstract class TranscriptAdapter
{
    /// <param name="module">The module's folder under <c>src/diagrams/</c>.</param>
    /// <param name="testProject">The test project that holds the transcript, in the module's <c>backend</c> folder.</param>
    /// <param name="fileName">The checked-in file's name, in that project's <c>Parity</c> folder.</param>
    protected TranscriptAdapter(string module, string testProject, string fileName)
    {
        TestProject = testProject;
        ModuleFolder = RepositoryPaths.Under("src", "diagrams", module);
        CheckedInPath = IoPath.Combine(ModuleFolder, "backend", testProject, "Parity", fileName);
        Generator = $"src/diagrams/{module}/backend/{testProject}/Parity/{GetType().Name}.cs";
    }

    /// <summary>The <c>src/</c> folder, which the corpus's paths are relative to.</summary>
    public string SourceFolder => IoPath.GetFullPath(IoPath.Combine(ModuleFolder, "..", ".."));

    /// <summary>The checked-in transcript.</summary>
    public string CheckedInPath { get; }

    /// <summary>The transcript's <c>generator</c> field: this adapter's source file.</summary>
    public string Generator { get; }

    /// <summary>The module's folder, <c>src/diagrams/&lt;module&gt;</c>.</summary>
    protected string ModuleFolder { get; }

    /// <summary>The test project that holds the transcript.</summary>
    protected string TestProject { get; }

    /// <summary>The transcript's <c>module</c> field: the diagram type's origin key.</summary>
    public abstract string Origin { get; }

    /// <summary>
    /// The toolbox, written once: it does not read the document.
    /// </summary>
    public abstract IReadOnlyList<ToolboxItemDefinition> Toolbox { get; }

    /// <summary>The corpus, in the order it is recorded.</summary>
    public abstract IReadOnlyList<TranscriptDocument> Corpus();

    /// <summary>
    /// Records one document the corpus has not recorded before: whatever the module's code answers
    /// for it, written into the document's object, which the harness has opened and closes.
    /// </summary>
    /// <param name="json">The transcript, inside the document's object.</param>
    /// <param name="document">The document.</param>
    /// <param name="bytes">Its bytes.</param>
    /// <param name="folder">An empty scratch folder of its own, deleted afterwards.</param>
    /// <param name="cancellationToken">The test's token.</param>
    public abstract Task WriteDocumentAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, string folder, CancellationToken cancellationToken);

    /// <summary>A document on disk, named by its path relative to <c>src/</c>.</summary>
    protected TranscriptDocument OnDisk(string relative) =>
        new(relative, cancellationToken => File.ReadAllBytesAsync(IoPath.Combine(SourceFolder, relative), cancellationToken));

    /// <summary><paramref name="paths"/> relative to <c>src/</c> with forward slashes, in ordinal order.</summary>
    protected IEnumerable<string> Relative(IEnumerable<string> paths) =>
        paths.Select(path => IoPath.GetRelativePath(SourceFolder, path).Replace('\\', '/')).Order(StringComparer.Ordinal);
}
