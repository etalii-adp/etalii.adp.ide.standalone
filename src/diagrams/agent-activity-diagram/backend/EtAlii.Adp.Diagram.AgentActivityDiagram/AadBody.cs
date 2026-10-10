using System.Text;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>What came of one edit of an activity file: applied, or refused with a reason for the user.</summary>
public readonly record struct AadEdit(string? Refusal)
{
    /// <summary>The edit was applied.</summary>
    public static AadEdit Applied { get; } = new(null);

    /// <summary>The edit was refused, and nothing was changed.</summary>
    public static AadEdit Refused(string because) => new(because);

    public bool WasApplied => Refusal is null;
}

/// <summary>
/// One activity file, open: its text, read through the bundled FBL binding and changed only by
/// splices that binding plans.
/// </summary>
/// <remarks>
/// <para>
/// <b>The module has no parser and no writer of its own</b> (agent-activity-diagram Requirement
/// 10.4). Reading is <see cref="OpenBody.Open"/>, an edit is a <see cref="ModelChange"/> the
/// library plans as splices, and the text between the splices is never touched - which is the
/// whole of Requirement 2.7, and why an agent's comments and key order survive an edit made here.
/// </para>
/// <para>
/// FBL leaves three things to the host, and they are supplied here once: the ids of the entries
/// that store none (the root is the diagram; a locked position and a group state are view data,
/// keyed by the element they name - DISL 0.4, <c>persistence.view.bind</c>), and which attributes
/// hold a moment in time, so that one is written plain as YAML reads it.
/// </para>
/// </remarks>
public sealed class AadBody
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(LoadBinding);

    /// <summary>The attributes the definition types as a date-time, as <c>rule.attribute</c>.</summary>
    private static readonly HashSet<string> TimeAttributes = new(StringComparer.Ordinal) { "task.updated", "pullRequest.updated" };

    private readonly OpenBody _body;

    // Each read is cached with the bytes it was read from, so a change makes it stale by itself.
    private (byte[] Bytes, FblModel Model)? _model;
    private (byte[] Bytes, DislModel Model)? _disl;

    private AadBody(OpenBody body)
    {
        _body = body;
    }

    /// <summary>The file's text, exactly as it would be written.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>The entries the binding read, with the reader's own findings.</summary>
    public FblModel Model
    {
        get
        {
            if (_model is not { } model || model.Bytes != _body.Bytes)
            {
                _model = model = (_body.Bytes, _body.Model);
            }

            return model.Model;
        }
    }

    /// <summary>The same entries as the definition's model: typed elements, their children and the relations derived from their keys.</summary>
    public DislModel Disl
    {
        get
        {
            if (_disl is not { } model || model.Bytes != _body.Bytes)
            {
                _disl = model = (_body.Bytes, DislModelBuilder.From(Model, AadDefinition.Specification));
            }

            return model.Model;
        }
    }

    /// <summary>Reads <paramref name="text"/> as an activity file. Never throws for what the file holds: what cannot be read is a finding.</summary>
    /// <summary>The schema version this host knows; a file of a newer one is shown and never written (Requirement 2.8).</summary>
    public const long KnownVersion = 1;

    /// <summary>The version the file declares, or the known one when it declares none that can be read.</summary>
    public long Version =>
        Model.Elements.FirstOrDefault(element => element.Type == "Diagram")?.Attributes.GetValueOrDefault("version") is long version ? version : KnownVersion;

    /// <summary>
    /// The entries of the reader's part that name an element the file no longer has: a lock or a
    /// group state left behind when an agent removed its element (Requirement 8.8).
    /// </summary>
    public IReadOnlyList<string> OrphanedViewEntries
    {
        get
        {
            var elements = Model.Elements.Where(element => element.Type is not ("Placement" or "GroupState" or "Diagram")).Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
            return
            [
                .. Model.Elements
                    .Where(element => element.Type is "Placement" or "GroupState")
                    .Where(element => element.Attributes.GetValueOrDefault("element") is not string named || !elements.Contains(named))
                    .Select(element => element.Id),
            ];
        }
    }

    public static AadBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var options = new FblOptions { FileName = "body.aad", DeriveId = DeriveId, TimeAttributes = TimeAttributes };
        return new AadBody(OpenBody.Open(Encoding.UTF8.GetBytes(text), LoadedBinding.Value, options));
    }

    /// <summary>Plans <paramref name="change"/> as splices and applies them, or says why not.</summary>
    internal AadEdit Change(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return _body.Change(change) is PlanResult.Refused refused ? AadEdit.Refused(refused.Reason) : AadEdit.Applied;
    }

    /// <summary>
    /// The id of an entry whose rule stores none. The names are the definition's (its research
    /// note R2): the fixtures list them, and the definition's operations address elements by them.
    /// </summary>
    private static string? DeriveId(IdRequest request) => request.Rule switch
    {
        "diagram" => "diagram",
        "placement" => $"pinned:{TextOf(request, "element")}",
        "group" => $"groups:{TextOf(request, "element")}#{TextOf(request, "group")}",
        _ => null,
    };

    private static string TextOf(IdRequest request, string attribute) =>
        request.Attributes.TryGetValue(attribute, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";

    private static FblBinding LoadBinding()
    {
        var assembly = typeof(AadBody).Assembly;
        using var stream = assembly.GetManifestResourceStream("agent-activity-diagram.fbl")
            ?? throw new InvalidOperationException("The agent activity diagram's FBL binding is not bundled in this module.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var problems = FblDocumentLoader.Load(buffer.ToArray(), out var document);
        return document is null
            ? throw new InvalidOperationException($"The agent activity diagram's FBL binding does not load: {string.Join("; ", problems)}")
            : document.Bindings["aad"];
    }
}
