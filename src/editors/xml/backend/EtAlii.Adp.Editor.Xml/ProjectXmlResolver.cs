using System.Text;
using System.Xml;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>
/// The one resolver every reader and every schema set of this module is given (xml-editor
/// Security): a reference is decided by <see cref="SchemaReferenceRules"/> against the folder of
/// the file that names it, and only a file of the project is ever read - through
/// <see cref="TextFileBuffer"/>, so encoding is decided where it is for every editor. A web
/// address, a <c>file:</c> address, an absolute path and a path that leaves the project are
/// refused with their reason, and nothing is fetched. What was read and what was refused is
/// recorded, so an answer can name the files it depended on and the references it could not use.
/// </summary>
internal sealed class ProjectXmlResolver : XmlResolver
{
    /// <summary>The scheme of the address a refused reference resolves to, so that reading it fails with its reason.</summary>
    private const string RefusedScheme = "adp-refused";

    private readonly string _rootPath;
    private readonly List<string> _read = [];
    private readonly List<string> _attempted = [];
    private readonly List<RefusedReference> _refused = [];

    public ProjectXmlResolver(string rootPath)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        _rootPath = Path.GetFullPath(rootPath);
    }

    /// <summary>The full paths of the project files read, in the order they were first read.</summary>
    public IReadOnlyList<string> Read => _read;

    /// <summary>
    /// The full paths of the project files asked for, read or not: a file that is missing now
    /// is one whose arrival changes the answer.
    /// </summary>
    public IReadOnlyList<string> Attempted => _attempted;

    /// <summary>The references refused, each with the reason said to the user.</summary>
    public IReadOnlyList<RefusedReference> Refused => _refused;

    public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
    {
        var reference = relativeUri ?? "";

        // A reader first makes the document's own address absolute, asking with no base: that
        // address is one this module gave it, and is answered as itself when it is in the project.
        if (baseUri is null && Uri.TryCreate(reference, UriKind.Absolute, out var absolute) && DocumentPathOf(absolute) is not null)
        {
            return absolute;
        }

        var documentPath = DocumentPathOf(baseUri);
        if (documentPath is null)
        {
            return Refuse(reference, "is not named by a file of the project");
        }

        return SchemaReferenceRules.Resolve(reference, documentPath, new HashSet<string>()) switch
        {
            SchemaTarget.Project project => new Uri(Path.Combine([_rootPath, .. project.Path])),
            SchemaTarget.None none => Refuse(reference, none.Reason),
            _ => Refuse(reference, "is not a file of the project"),
        };
    }

    public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        ArgumentNullException.ThrowIfNull(absoluteUri);

        if (absoluteUri.Scheme == RefusedScheme)
        {
            var refused = _refused[int.Parse(absoluteUri.AbsolutePath, System.Globalization.CultureInfo.InvariantCulture)];
            throw new XmlException($"'{refused.Reference}' {refused.Reason}.");
        }

        // Checked again here rather than trusted from ResolveUri: a reader may ask for an entity
        // by an address it built itself, and nothing outside the project is read whoever asks.
        var fullPath = absoluteUri.IsFile ? Path.GetFullPath(absoluteUri.LocalPath) : "";
        if (fullPath.Length == 0 || !IsInsideRoot(fullPath))
        {
            throw new XmlException($"'{absoluteUri.OriginalString}' is not a file of the project.");
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(ReadText(fullPath)), writable: false);
    }

    /// <summary>
    /// The text of a file of the project, read through <see cref="TextFileBuffer"/> and recorded;
    /// an <see cref="XmlException"/> with the buffer's reason when it cannot be read.
    /// </summary>
    public string ReadText(string fullPath)
    {
        fullPath = Path.GetFullPath(fullPath);
        if (!IsInsideRoot(fullPath))
        {
            throw new XmlException($"'{fullPath}' is not a file of the project.");
        }

        if (!_attempted.Contains(fullPath, StringComparer.Ordinal))
        {
            _attempted.Add(fullPath);
        }

        var opened = TextFileBuffer.Open(fullPath);
        if (opened.Buffer is null)
        {
            throw new XmlException(opened.Refusal);
        }

        if (!_read.Contains(fullPath, StringComparer.Ordinal))
        {
            _read.Add(fullPath);
        }

        return opened.Buffer.Content;
    }

    /// <summary>The full path of a file of the project, from its path segments.</summary>
    public string FullPathOf(IReadOnlyList<string> projectPath) => Path.Combine([_rootPath, .. projectPath]);

    /// <summary>The project path of a file, its file name last, or null when it is outside the root.</summary>
    public string[]? ProjectPathOf(string fullPath) => DocumentPathOf(new Uri(Path.GetFullPath(fullPath)));

    /// <summary>The address a document at <paramref name="fullPath"/> is read under, so its references resolve against its folder.</summary>
    public static string BaseUriOf(string fullPath) => new Uri(Path.GetFullPath(fullPath)).AbsoluteUri;

    private Uri Refuse(string reference, string reason)
    {
        _refused.Add(new RefusedReference(reference, reason));
        return new Uri($"{RefusedScheme}:{_refused.Count - 1}");
    }

    /// <summary>The project path of the file a base address names, or null when it names none inside the root.</summary>
    private string[]? DocumentPathOf(Uri? baseUri)
    {
        if (baseUri is null || !baseUri.IsAbsoluteUri || !baseUri.IsFile)
        {
            return null;
        }

        var fullPath = Path.GetFullPath(baseUri.LocalPath);
        return IsInsideRoot(fullPath)
            ? Path.GetRelativePath(_rootPath, fullPath).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : null;
    }

    private bool IsInsideRoot(string fullPath)
    {
        var relative = Path.GetRelativePath(_rootPath, fullPath);
        return relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}

/// <summary>A reference the resolver would not read, and why, in words said after the reference.</summary>
internal sealed record RefusedReference(string Reference, string Reason);
