using EtAlii.Adp.Documents;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Finds the file a <c>template:</c> or <c>extends:</c> reference names, within the workspace and
/// nowhere else, and remembers what it read.
/// </summary>
/// <remarks>
/// <para>
/// The workspace boundary is enforced here rather than trusted from above, because a pipeline file
/// is written by whoever opened the project and <c>template: ../../../../etc/passwd</c> is a
/// perfectly ordinary-looking line. A reference that leads out is refused and reported, never
/// followed - so a diagram cannot be turned into a way to read arbitrary files off the machine.
/// </para>
/// <para>
/// One instance per document store, so a template shared by twenty pipelines is read once
/// (Requirement 5 - and the reason the cache is keyed by the resolved absolute path rather than by
/// the reference text, which differs between the files that reference it).
/// </para>
/// </remarks>
public sealed class PipelineTemplates
{
    private static readonly ILogger _logger = Log.ForContext<PipelineTemplates>();

    private readonly Dictionary<string, LineDocument?> _documents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a resolver bounded by <paramref name="workspaceRoot"/>.</summary>
    public PipelineTemplates(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        WorkspaceRoot = IoPath.TrimEndingDirectorySeparator(IoPath.GetFullPath(workspaceRoot));
    }

    /// <summary>The only directory tree this will read from.</summary>
    public string WorkspaceRoot { get; }

    /// <summary>
    /// Where <paramref name="reference"/> points, as an absolute path - or why it was not followed.
    /// </summary>
    /// <param name="reference">The reference, as the file wrote it.</param>
    /// <param name="referencingPath">The file that made the reference, absolute.</param>
    public (string Path, PipelineTemplateUnresolvedReason Reason) Locate(
        PipelineTemplateReference reference,
        string referencingPath)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (reference.Resource.Length > 0)
        {
            return ("", PipelineTemplateUnresolvedReason.OtherRepository);
        }

        if (reference.Path.Length == 0 || ContainsExpression(reference.Path))
        {
            return ("", PipelineTemplateUnresolvedReason.ParameterDependent);
        }

        // Azure reads a leading slash as repository-root-relative; everything else is relative to
        // the file that wrote it. Any other rooted path is off this map entirely.
        var raw = reference.Path.Replace('\\', '/');
        string combined;
        if (raw.StartsWith('/'))
        {
            combined = IoPath.Combine(WorkspaceRoot, raw.TrimStart('/'));
        }
        else if (IoPath.IsPathRooted(raw) || IsWindowsAbsolute(raw))
        {
            // Judged portably, not by the host platform: a pipeline file travels between
            // machines, and "C:/Windows/win.ini" is an absolute path wherever the YAML is
            // parsed - on Linux, IsPathRooted alone reads it as a workspace-relative name
            // and misreports the escape attempt as merely NotFound.
            return ("", PipelineTemplateUnresolvedReason.OutsideWorkspace);
        }
        else
        {
            combined = IoPath.Combine(IoPath.GetDirectoryName(referencingPath) ?? WorkspaceRoot, raw);
        }

        var full = IoPath.GetFullPath(combined);
        if (!IsInsideWorkspace(full))
        {
            _logger.Debug("Refusing template {Reference}: {Path} is outside {Workspace}", reference.Reference, full, WorkspaceRoot);
            return ("", PipelineTemplateUnresolvedReason.OutsideWorkspace);
        }

        return File.Exists(full)
            ? (full, PipelineTemplateUnresolvedReason.None)
            : ("", PipelineTemplateUnresolvedReason.NotFound);
    }

    /// <summary>A Windows drive path (<c>C:/…</c>) or UNC path (<c>//server/…</c>), absolute on any platform.</summary>
    private static bool IsWindowsAbsolute(string raw) =>
        (raw.Length >= 2 && char.IsAsciiLetter(raw[0]) && raw[1] == ':') || raw.StartsWith("//", StringComparison.Ordinal);

    /// <summary>
    /// The document at <paramref name="path"/>, read once and kept. Null where it could not be
    /// read - which is recorded as a failure like any other rather than thrown, since one broken
    /// template must not take the whole diagram down.
    /// </summary>
    public LineDocument? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_documents.TryGetValue(path, out var cached))
        {
            return cached;
        }

        LineDocument? document;
        try
        {
            document = LineDocument.Parse(SharedDocumentReader.ReadAllText(path));
        }
        catch (IOException error)
        {
            _logger.Debug(error, "A template at {Path} could not be read", path);
            document = null;
        }
        catch (UnauthorizedAccessException error)
        {
            _logger.Debug(error, "A template at {Path} could not be read", path);
            document = null;
        }

        _documents[path] = document;
        return document;
    }

    /// <summary>Forgets everything read, for when the files on disk have changed under it.</summary>
    public void Forget() => _documents.Clear();

    /// <summary>
    /// Whether a resolved path really is under the workspace root.
    /// </summary>
    /// <remarks>
    /// Compared as a path prefix with a separator, so a sibling directory whose name merely starts
    /// with the workspace's - <c>work</c> and <c>workspace-backup</c> - is not mistaken for being
    /// inside it.
    /// </remarks>
    private bool IsInsideWorkspace(string fullPath) =>
        fullPath.Equals(WorkspaceRoot, StringComparison.OrdinalIgnoreCase) ||
        fullPath.StartsWith(WorkspaceRoot + IoPath.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a path is decided at compile or run time rather than written out - the three
    /// expression syntaxes Azure Pipelines uses.
    /// </summary>
    private static bool ContainsExpression(string path) =>
        path.Contains("${{", StringComparison.Ordinal) ||
        path.Contains("$(", StringComparison.Ordinal) ||
        path.Contains("$[", StringComparison.Ordinal);
}
