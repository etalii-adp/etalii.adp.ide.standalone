using System.Text;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One <c>.rq</c> body, read through FBL: the module's binding (<c>sparql.fbl</c>, embedded), whose reader is the
/// persistence plugin <see cref="SparqlQueryPlugin"/>, and the DISL model built from what it reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>It reads and nothing else.</b> There is no <c>Change</c>, no <c>Save</c> and no way to set its bytes: a query is
/// edited in a text editor, and the diagram follows the file. Two stops refuse a write independently of this class -
/// the binding's <c>readOnly</c>, which <see cref="PluginBody"/> checks before it asks the plugin, and the plugin's
/// <see cref="SparqlQueryPlugin.Plan"/>, which refuses every change - and <c>NoWriterSurface.Tests</c> holds that no
/// code here calls a body's save or plans a splice.
/// </para>
/// <para>
/// <b>A text that does not parse is an unreadable body</b>, with no records and the plugin's one <c>std.unparseable</c>:
/// the diagram shows the frame only (Q2), and the definition words the finding.
/// </para>
/// </remarks>
public sealed class SparqlBody
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(SparqlBody).Assembly, "sparql.fbl", "query"));

    private readonly PluginBody _body;

    private readonly Lazy<DislModel> _disl;

    private SparqlBody(PluginBody body)
    {
        _body = body;
        _disl = new Lazy<DislModel>(() => SparqlDefinition.Model(Reading));
    }

    /// <summary>The binding every query body is read with.</summary>
    public static FblBinding Binding => LoadedBinding.Value;

    /// <summary>What the plugin read from the body: its stored records, or none and the reason it could not.</summary>
    public FblModel Reading => _body.Model;

    /// <summary>Whether the plugin could not read the body, which then draws the frame only.</summary>
    public bool IsUnreadable => Reading.Unreadable;

    /// <summary>
    /// The DISL model of what the plugin read (<see cref="DislModelBuilder"/>, under the bundled definition), built once,
    /// when first asked for.
    /// </summary>
    public DislModel Disl => _disl.Value;

    /// <summary>A body for <paramref name="text"/>. Never throws on content.</summary>
    public static SparqlBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SparqlBody(PluginBody.Open(Encoding.UTF8.GetBytes(text), Binding, new SparqlQueryPlugin(), "query.rq"));
    }
}
