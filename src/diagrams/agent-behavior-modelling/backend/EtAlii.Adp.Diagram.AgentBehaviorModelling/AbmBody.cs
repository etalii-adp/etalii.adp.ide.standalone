using System.Text;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// One Markdown body of a behavior model, read and written through FBL: the module's binding
/// (<c>agent-behavior-modelling.fbl</c>, embedded), whose reader is the persistence plugin
/// <see cref="AbmMarkdownPlugin"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every write is a <see cref="ModelChange"/></b>, planned by the plugin as the lines it touches and
/// applied by the library, so an unchanged document stays byte-identical and everything outside the
/// tree is the author's.
/// </para>
/// <para>
/// <b>Undo stays the host's</b>, as for the hype cycle graph: the shared restore command puts the text
/// back. The library's own history is per open body, and a command opens a fresh one each time.
/// </para>
/// </remarks>
public sealed class AbmBody : ITextBody<AbmBody>
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(AbmBody).Assembly, "agent-behavior-modelling.fbl", "abm"));

    private readonly PluginBody _body;

    private readonly TextMemo<AbmModel> _model = new();
    private readonly TextMemo<DislModel> _disl = new();

    private AbmBody(PluginBody body)
    {
        _body = body;
    }

    /// <summary>The binding every behavior model body is read and written with.</summary>
    private static FblBinding Binding => LoadedBinding.Value;

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>The body's bytes after every change made to it.</summary>
    public byte[] Bytes => _body.Bytes;

    /// <summary>What the plugin reads from the body now.</summary>
    public FblModel Reading => _body.Model;

    /// <summary>The tree as the module's parser reads it now: what the canvas, the layout and the commands address.</summary>
    public AbmModel Model => _model.Of(_body.Bytes, () => AbmParser.Parse(LineDocument.Parse(Text)));

    /// <summary>
    /// The DISL model of what the plugin reads now (<see cref="DislModelBuilder"/>, under the bundled
    /// definition), its ids derived by the definition's rule.
    /// </summary>
    public DislModel Disl => _disl.Of(_body.Bytes, () => DislModelBuilder.From(_body.Model, AbmDefinition.Specification));

    /// <summary>A body for <paramref name="text"/>. Never throws on content.</summary>
    public static AbmBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Open(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>A body for <paramref name="bytes"/>, as they are. Never throws on content.</summary>
    public static AbmBody Open(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new AbmBody(PluginBody.Open(bytes, Binding, new AbmMarkdownPlugin(), "body.md"));
    }

    /// <summary>Plans and applies one change; the refusal's sentence when the plugin refuses it.</summary>
    public EditOutcome Change(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return _body.Change(change) is PlanResult.Refused refused ? EditOutcome.Refused(refused.Reason) : EditOutcome.Applied;
    }

}
