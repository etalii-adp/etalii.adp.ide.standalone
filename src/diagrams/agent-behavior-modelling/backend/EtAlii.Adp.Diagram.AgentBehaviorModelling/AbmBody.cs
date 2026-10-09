using System.Text;
using EtAlii.Adp.Documents;
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
public sealed class AbmBody
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(LoadBinding);

    private readonly PluginBody _body;

    // Each read is cached with the bytes it was read from, so a change makes it stale by itself.
    private (byte[] Bytes, AbmModel Model)? _model;
    private (byte[] Bytes, DislModel Model)? _disl;

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
    public AbmModel Model
    {
        get
        {
            if (_model is not { } model || model.Bytes != _body.Bytes)
            {
                _model = model = (_body.Bytes, AbmParser.Parse(LineDocument.Parse(Text)));
            }

            return model.Model;
        }
    }

    /// <summary>
    /// The DISL model of what the plugin reads now (<see cref="DislModelBuilder"/>, under the bundled
    /// definition), its ids derived by the definition's rule.
    /// </summary>
    public DislModel Disl
    {
        get
        {
            if (_disl is not { } model || model.Bytes != _body.Bytes)
            {
                _disl = model = (_body.Bytes, DislModelBuilder.From(_body.Model, AbmDefinition.Specification));
            }

            return model.Model;
        }
    }

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
    public AbmEdit Change(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return _body.Change(change) is PlanResult.Refused refused ? AbmEdit.Refused(refused.Reason) : AbmEdit.Applied;
    }

    private static FblBinding LoadBinding()
    {
        var assembly = typeof(AbmBody).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith("agent-behavior-modelling.fbl", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var problems = FblDocumentLoader.Load(buffer.ToArray(), out var document);
        return document is null ? throw new InvalidOperationException($"The behavior model's FBL binding does not load: {string.Join("; ", problems)}") : document.Bindings["abm"];
    }
}
