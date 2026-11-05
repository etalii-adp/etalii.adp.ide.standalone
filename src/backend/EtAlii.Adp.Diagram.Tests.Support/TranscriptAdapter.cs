using System.Text.Json;
using EtAlii.Adp.Tests.Support;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Tests.Support;

/// <summary>
/// What one module tells <see cref="TranscriptHarness"/>: where its transcript lives, its toolbox,
/// its corpus, and what it records for one document. Everything else - the header, the documents
/// array, a document recorded once and then as <c>sameAs</c>, the scratch folder, the closing line
/// ending - is the harness's, the same for every module.
/// </summary>
/// <remarks>
/// <para>
/// <b>The adapter is named for its file.</b> The transcript's <c>generator</c> field names
/// <c>Parity/&lt;adapter type&gt;.cs</c>, so a module's adapter keeps the name its generator always
/// had and the field stays as recorded.
/// </para>
/// <para>
/// <b>The virtual members are the frame's few variations</b>, each defaulting to what most modules
/// write: what is recorded once around the toolbox, what follows the documents, the names of the
/// array and of its entries, and what identifies a document and makes a later one <c>sameAs</c> it.
/// A module whose transcript was frozen with one of them overrides it, so the file stays as recorded.
/// </para>
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
    /// The toolbox, written once: it does not read the document. <see langword="null"/> for a
    /// transcript that has no <c>toolbox</c> field at all.
    /// </summary>
    protected abstract IReadOnlyList<ToolboxItemDefinition>? Toolbox { get; }

    /// <summary>The name of the transcript's array of documents.</summary>
    public virtual string DocumentsName => "documents";

    /// <summary>The name of the field that opens each entry of that array with the document's name.</summary>
    public virtual string DocumentName => "document";

    /// <summary>The corpus, in the order it is recorded.</summary>
    public abstract IReadOnlyList<TranscriptDocument> Corpus();

    /// <summary>
    /// Writes what is recorded once, after the header and before the documents: the toolbox, unless
    /// it is <see langword="null"/>. A module that records more around it overrides this and writes
    /// the toolbox where its transcript has it by calling this one.
    /// </summary>
    /// <param name="json">The transcript, inside its root object.</param>
    /// <param name="cancellationToken">The test's token.</param>
    public virtual Task WriteBeforeDocumentsAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        cancellationToken.ThrowIfCancellationRequested();
        if (Toolbox is { } toolbox)
        {
            json.WriteStartArray("toolbox");
            foreach (var item in toolbox)
            {
                json.WriteStringValue($"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}");
            }

            json.WriteEndArray();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes what identifies a document, after its name, and answers what a later document must
    /// equal to be written as <c>sameAs</c> it: by default the <c>sha256</c> of its bytes, which is
    /// also the answer. A module for which equal bytes are not yet the same reading answers more;
    /// <see langword="null"/> means this document is recorded in full and no later one is the same.
    /// </summary>
    /// <param name="json">The transcript, inside the document's object.</param>
    /// <param name="document">The document.</param>
    /// <param name="bytes">Its bytes.</param>
    /// <param name="cancellationToken">The test's token.</param>
    public virtual Task<string?> WriteIdentityAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        cancellationToken.ThrowIfCancellationRequested();
        var sha = TranscriptText.Sha256(bytes);
        json.WriteString("sha256", sha);
        return Task.FromResult<string?>(sha);
    }

    /// <summary>Writes what is recorded once after the documents; by default nothing.</summary>
    /// <param name="json">The transcript, inside its root object.</param>
    /// <param name="cancellationToken">The test's token.</param>
    public virtual Task WriteAfterDocumentsAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

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
