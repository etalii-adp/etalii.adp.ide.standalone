using System.Text;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// One Databricks configuration file, read through FBL: the module's one binding (<c>databricks.fbl</c>, embedded),
/// applied by <c>EtAlii.Adp.Specification.Fbl</c> as three readings of one open body (FBL 0.10, sections 5.1, 8.2 and
/// 9.1), the bundle's, a job's and a pipeline's (databricks-disl-fbl Requirement 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this module parses the file any more.</b> Each reading is the library's reading of the rules its
/// origin admits; a job's and a pipeline's select one resource by the binding's capture for that origin, and the
/// bundle's selects none.
/// </para>
/// <para>
/// <b>Nothing in this module writes the file either</b>: every edit is a <see cref="ModelChange"/> the library plans as
/// splices in the file's own conventions (FBL section 6, Requirement 6), in the reading the edit is made in, so a value
/// changes only its span and comments, unknown keys and blank lines survive because no splice touches them.
/// </para>
/// <para>
/// <b>A text read recently is not read again</b>: reading is a pure function of the bytes, so <see cref="Open"/> forks
/// the open body of a text it read lately (<see cref="OpenBody.Fork"/>), as the hype cycle's body does.
/// </para>
/// </remarks>
internal sealed class DatabricksBody
{
    /// <summary>The origin of the bundle's reading.</summary>
    public const string BundleOrigin = "databricks/bundle";

    /// <summary>The origin of a job's reading.</summary>
    public const string JobOrigin = "databricks/job";

    /// <summary>The origin of a pipeline's reading.</summary>
    public const string PipelineOrigin = "databricks/pipeline";

    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(DatabricksBody).Assembly, "databricks.fbl", "configuration"));

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;
    private List<int> _lineStarts;
    private string[] _lines;

    private DatabricksBody(OpenBody body)
    {
        _body = body;
        _lineStarts = LineStarts(body.Bytes);
        _lines = Encoding.UTF8.GetString(body.Bytes).Split('\n');
    }

    /// <summary>The binding every Databricks file is read and written with.</summary>
    public static FblBinding Binding => LoadedBinding.Value;

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>A body for <paramref name="text"/>. Never throws on content: a body that is not read reads as unreadable.</summary>
    public static DatabricksBody Open(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (Recent.Find(bytes) is { } known) return new DatabricksBody(known.Fork());

        var body = OpenBody.Open(bytes, Binding, new FblOptions { FileName = IsJson(text) ? "databricks.json" : "databricks.yml", DeriveId = DatabricksDefinition.ReadIds });
        Recent.Remember(body.Fork());
        return new DatabricksBody(body);
    }

    /// <summary>
    /// The resource the registration at <paramref name="registrationPath"/> selects by its <c>resource</c> header, read by
    /// FBL (§8.1, 0.10 §8.2); null when it names none, or when there is no registration or it cannot be read, in which case
    /// a job's or a pipeline's reading shows the first of its capture.
    /// </summary>
    /// <remarks>
    /// Read shared, per the repository's shared-read discipline: a rename rewrites a registration's headers in place, and
    /// a default-share reader would refuse that write on Windows, and be refused by it.
    /// </remarks>
    public static string? ResourceOf(string? registrationPath)
    {
        if (registrationPath is not { Length: > 0 }) return null;
        try
        {
            return OpenRegistration.Open(SharedDocumentReader.ReadAllBytes(registrationPath)).Resource;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The reading of <paramref name="origin"/> that selects <paramref name="resource"/>, or the first of its capture for null.</summary>
    public FblModel Reading(string origin, string? resource = null) => _body.ModelOf(origin, resource);

    /// <summary>
    /// Plans <paramref name="change"/> in the reading of <paramref name="origin"/> that selects <paramref name="resource"/>
    /// and applies it as splices (FBL 0.10 sections 6 and 9.1): the refusal's sentence when the library refuses it, and
    /// nothing written then.
    /// </summary>
    public EditOutcome Change(ModelChange change, string origin, string? resource)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (_body.Change(change, origin, resource) is PlanResult.Refused refused) return EditOutcome.Refused(refused.Reason);

        _lineStarts = LineStarts(_body.Bytes);
        _lines = Text.Split('\n');
        Recent.Remember(_body.Fork());
        return EditOutcome.Applied;
    }

    /// <summary>The resources the reading of <paramref name="origin"/> selects among, in document order.</summary>
    public IReadOnlyList<string> Resources(string origin) => Reading(origin).Resources;

    /// <summary>
    /// Whether <paramref name="text"/> is read as JSON rather than YAML (Requirement 4.6): the module has always told the
    /// two apart by the first character that is not white space, so a settings file named <c>.yml</c> that holds JSON
    /// reads as JSON.
    /// </summary>
    private static bool IsJson(string text) => text.TrimStart().StartsWith('{');

    /// <summary>The 0-based lines <paramref name="span"/> is written on, as the module has always addressed an entry.</summary>
    /// <remarks>
    /// They keep the shape the module has always given them (<c>YamlNodeRange</c>), which the validator's line locations
    /// and the source resolver read: an entry ends on its last line that says something, so a line that only closes a
    /// flow collection, or is blank or a comment, is not its end.
    /// </remarks>
    public LineRange LinesOf(Span span)
    {
        var start = LineOf(span.Start);
        var end = Math.Max(start, LineOf(Math.Max(span.Start, span.End - 1)));
        while (end > start && SaysNothing(_lines[end])) end--;
        return new LineRange(start, end);
    }

    private static bool SaysNothing(string line)
    {
        var text = line.Trim(' ', '\t', '\r');
        return text.Length == 0 || text.StartsWith('#') || text.All(character => character is ']' or '}' or ',');
    }

    private int LineOf(int offset)
    {
        var index = _lineStarts.BinarySearch(offset);
        return index >= 0 ? index : ~index - 1;
    }

    private static List<int> LineStarts(byte[] bytes)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\n' && index + 1 < bytes.Length) starts.Add(index + 1);
        }

        return starts;
    }
}
