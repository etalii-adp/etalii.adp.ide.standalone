using System.Runtime.CompilerServices;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// One <c>.yml</c> body of an Azure DevOps pipeline, read through FBL: the module's binding
/// (<c>azure-devops-pipeline.fbl</c>, embedded), whose reader is the persistence plugin
/// <see cref="PipelineYamlPlugin"/>, handed the body's <see cref="PipelineReadContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reading only, for now</b> (azure-devops-pipeline-disl-fbl task 8): a pipeline is still written by
/// <see cref="PipelineWriter"/> on the store's <see cref="LineDocument"/>, and every edit opens a new body
/// over the bytes it leaves, so the bytes of one body never change.
/// </para>
/// <para>
/// <b>The store's entry keeps the parser's model</b>, which the canvas, the layout and the commands
/// address, and the body that read it is found from it (<see cref="ReadingOf"/>), which is how the
/// definition derives the menus and the rows from the DISL model of the same reading.
/// </para>
/// </remarks>
internal sealed class PipelineBody : ITextBody<PipelineBody>
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(PipelineBody).Assembly, "azure-devops-pipeline.fbl", "pipeline"));

    /// <summary>The body that read each model, for as long as the model is held.</summary>
    private static readonly ConditionalWeakTable<PipelineModel, PipelineBody> _readers = [];

    private readonly PluginBody _body;
    private readonly PipelineYamlPlugin _plugin;
    private readonly Lazy<DislModel> _disl;

    private PipelineBody(PluginBody body, PipelineYamlPlugin plugin)
    {
        _body = body;
        _plugin = plugin;
        _disl = new Lazy<DislModel>(() => DislModelBuilder.From(_body.Model, PipelineDefinition.Specification));
    }

    /// <summary>The binding every pipeline body is read with.</summary>
    private static FblBinding Binding => LoadedBinding.Value;

    /// <summary>The body's text.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>The body's bytes.</summary>
    public byte[] Bytes => _body.Bytes;

    /// <summary>What the plugin reads from the body.</summary>
    public FblModel Reading => _body.Model;

    /// <summary>Whether nothing may be written to the body: a body that is not YAML is never written.</summary>
    public bool IsReadOnly => _body.IsReadOnly;

    /// <summary>The pipeline as the module's parser read it, followed templates in place: what the canvas, the layout and the commands address.</summary>
    public PipelineModel Model => _plugin.Model;

    /// <summary>
    /// The DISL model of what the plugin reads (<see cref="DislModelBuilder"/>, under the bundled definition),
    /// its ids derived by the definition's rules; built the first time it is asked for.
    /// </summary>
    public DislModel Disl => _disl.Value;

    /// <summary>
    /// What the store keeps of the body: its model, or, for a body that is not YAML, the empty model, the
    /// reader's message on the line it names, and that line and message as the reason nothing is written to it.
    /// </summary>
    public ParseOutcome<PipelineModel> Outcome =>
        Unparseable is { } finding
            ? new ParseOutcome<PipelineModel>(Model, [new ParseProblem(finding.Line - 1, finding.Reason)], $"Line {finding.Line}: {finding.Reason}")
            : new ParseOutcome<PipelineModel>(Model, []);

    /// <summary>What the definition's findings are evaluated with beside the model: the reader's one finding for a body that is not YAML.</summary>
    public IReadOnlyList<DislReaderFinding> ReaderFindings =>
        Unparseable is { } finding ? [DislReaderFinding.NotParsed(finding.Reason, finding.Line)] : [];

    /// <summary>
    /// The text of a new pipeline named <paramref name="baseName"/>: the binding's <c>template.text</c> with
    /// <c>{base}</c> replaced (FBL 13), which is the jobs-only pipeline the tool has always created (finding B17).
    /// </summary>
    public static string NewDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        // As a file name with the extension, so that a base name with a dot in it is the base name whole.
        return TemplateWriter.Replace(Binding.Template!.Text, baseName + ".yml", _ => "");
    }

    /// <summary>A body for <paramref name="text"/>, read without following a template. Never throws on content.</summary>
    public static PipelineBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Open(Encoding.UTF8.GetBytes(text), PipelineReadContext.Unbound);
    }

    /// <summary>A body for <paramref name="bytes"/>, as they are, with its templates followed as <paramref name="context"/> allows. Never throws on content.</summary>
    public static PipelineBody Open(byte[] bytes, PipelineReadContext context)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(context);
        var plugin = new PipelineYamlPlugin(context);
        var body = new PipelineBody(PluginBody.Open(bytes, Binding, plugin, "body.yml"), plugin);
        _readers.AddOrUpdate(body.Model, body);
        return body;
    }

    /// <summary>The body that read <paramref name="model"/>.</summary>
    /// <exception cref="InvalidOperationException">The model was not read through a body, as every model the store holds is.</exception>
    public static PipelineBody ReadingOf(PipelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _readers.TryGetValue(model, out var body)
            ? body
            : throw new InvalidOperationException("This pipeline model was not read through a pipeline body.");
    }

    /// <summary>The reader's finding that the body is not YAML, with its line and the reader's own message; null for a body that is.</summary>
    private (int Line, string Reason)? Unparseable =>
        Reading.Findings.FirstOrDefault(each => each.Code == PipelineValidator.UnparseableRuleId) is { } finding
            ? (finding.Location?.Line ?? 1, finding.Detail?.GetValueOrDefault(PipelineYamlPlugin.ReasonDetail) ?? finding.Message)
            : null;
}
