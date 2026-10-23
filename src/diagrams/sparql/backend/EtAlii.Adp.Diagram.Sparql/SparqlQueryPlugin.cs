using System.Text;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The FBL §11 persistence plugin of the binding <c>sparql.fbl#query</c> (<c>net.etalii.adp.w3c.sparqlQuery</c>): it reads
/// a query with <see cref="SparqlTokenizer"/> and <see cref="SparqlParser"/> and delivers its stored records with their
/// spans (<see cref="SparqlRecords"/>), and it plans nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a plugin.</b> SPARQL is no family FBL declares rules over: the prologue's prefixes, nested group patterns,
/// property paths and subqueries are a grammar, and the module's parser, with its three tiers, is that grammar
/// (FBL §11.5). The plugin is named in the definition's companion beside that reason.
/// </para>
/// <para>
/// <b>Reading never fails on content</b> (FBL §11.2). A text the tokenizer or the parser cannot read is no records,
/// unreadable, and one <c>std.unparseable</c> carrying the parser's sentence at the line and column it stopped at; the
/// definition words it.
/// </para>
/// <para>
/// <b>It refuses every plan, <see cref="ModelChange.Save"/> included,</b> with the binding's own <c>readOnly</c>
/// sentence. That is the second of two independent stops: <see cref="PluginBody"/> refuses a read-only binding's plan
/// before it asks the plugin at all, so a write needs both the binding and this class to change.
/// </para>
/// </remarks>
public sealed class SparqlQueryPlugin : IPersistencePlugin
{
    /// <summary>The plugin's id, as the binding's <c>reader.plugin</c> names it.</summary>
    private const string PluginId = "net.etalii.adp.w3c.sparqlQuery";

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public PluginReadResult Read(PluginReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = Encoding.UTF8.GetString(Body(request.Files));
        try
        {
            return new PluginReadResult(SparqlRecords.From(SparqlParser.Parse(text), text).Elements, [], false);
        }
        catch (SparqlParseException exception)
        {
            var location = new SourceLocation("", Math.Max(exception.Line, 1), Math.Max(exception.Column, 1), 0);
            return new PluginReadResult([], [new Finding(FindingCodes.Unparseable, FindingSeverity.Error, exception.Message, location)], true);
        }
    }

    /// <inheritdoc />
    public PluginPlanResult Plan(PluginPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PluginPlanResult.Refused(SparqlBody.Binding.ReadOnly!);
    }

    /// <inheritdoc />
    /// <remarks>Never asked, because the binding has <c>template.text</c> (finding B8); it answers with that text.</remarks>
    public byte[] Template(PluginTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Encoding.UTF8.GetBytes(SparqlBody.Binding.Template!.Text);
    }

    /// <inheritdoc />
    /// <remarks>A query depends on nothing beyond its own file.</remarks>
    public IReadOnlyList<string> Watch(PluginReadResult last) => [];

    /// <summary>The body's bytes: the one file a file body is handed as.</summary>
    private static byte[] Body(IReadOnlyList<PluginFile> files) =>
        files.Count > 0 ? files[0].Bytes : throw new ArgumentException("A query is read from one file.", nameof(files));
}
