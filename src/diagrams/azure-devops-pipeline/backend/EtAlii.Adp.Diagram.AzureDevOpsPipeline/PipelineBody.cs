using System.Runtime.CompilerServices;
using System.Text;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
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
/// <b>Every write is a <see cref="ModelChange"/></b>, planned by the plugin's <see cref="PipelinePlanner"/> as
/// splices of the spans it touches and applied by the library: one at a time (<see cref="Change"/>), several as one
/// edit (<see cref="Batch"/>), or a command's changes as one edit that is refused whole (<see cref="Write"/>). Every
/// command of the module writes through <see cref="Write"/> (azure-devops-pipeline-disl-fbl task 10), and its undo
/// puts back what that edit replaced (<see cref="Restore"/>).
/// </para>
/// <para>
/// <b>Undo stays the host's</b>, as for the behavior model: the library's own history is per open body, and a reload
/// clears it, so the host's command carries the library's inverse splices and the digest of the bytes it expects
/// (<c>RestorePipelineSpanCommand</c>, amendment 2).
/// </para>
/// <para>
/// <b>The store's entry is the body</b>: its bytes are what is saved and its <see cref="Model"/> is the parser's
/// model, which the canvas and the layout address. The body that read a model is found from it
/// (<see cref="ReadingOf"/>), which is how the definition derives the menus and the rows from the DISL model of
/// the same reading.
/// </para>
/// </remarks>
public sealed class PipelineBody : ITextBody<PipelineBody>
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(PipelineBody).Assembly, "azure-devops-pipeline.fbl", "pipeline"));

    /// <summary>The body that read each model, for as long as the model is held.</summary>
    private static readonly ConditionalWeakTable<PipelineModel, PipelineBody> _readers = [];

    private readonly PluginBody _body;
    private readonly PipelineYamlPlugin _plugin;
    private readonly TextMemo<DislModel> _disl = new();
    private readonly TextMemo<IReadOnlyDictionary<string, string>> _readIds = new();

    // While a batch is open, the planner every change of it is planned by, against the body as it was.
    private PipelinePlanner? _batch;

    private PipelineBody(PluginBody body, PipelineYamlPlugin plugin)
    {
        _body = body;
        _plugin = plugin;
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
    /// The DISL model of what the plugin reads now (<see cref="DislModelBuilder"/>, under the bundled definition),
    /// its ids derived by the definition's rules; built the first time it is asked for after each change.
    /// </summary>
    public DislModel Disl => _disl.Of(_body.Bytes, () => DislModelBuilder.From(_body.Model, PipelineDefinition.Specification));

    /// <summary>The edit <see cref="Write"/> or <see cref="Restore"/> applied last, its offsets in the bytes before it; null before either.</summary>
    public SpliceEdit? LastEdit { get; private set; }

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
    internal static PipelineBody Open(byte[] bytes, PipelineReadContext context)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(context);
        var plugin = new PipelineYamlPlugin(context);
        var body = new PipelineBody(PluginBody.Open(bytes, Binding, plugin, "body.yml"), plugin);
        _readers.AddOrUpdate(body.Model, body);
        return body;
    }

    /// <summary>Plans and applies one change; the refusal's sentence when the plugin refuses it.</summary>
    /// <remarks>Inside <see cref="Batch"/> the change is planned and kept, and applied with the others when the batch ends.</remarks>
    public EditOutcome Change(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (_batch is not null)
        {
            return _batch.Plan(change) is { } declined ? EditOutcome.Refused(declined) : EditOutcome.Applied;
        }

        var result = _body.Change(change);
        Remember();
        return result is PlanResult.Refused refused ? EditOutcome.Refused(refused.Reason) : EditOutcome.Applied;
    }

    /// <summary>
    /// Runs <paramref name="writes"/>, whose <see cref="Change"/>s are planned together against the body as it is
    /// now, and applies them as one edit: a new stage is three adds (the stage, its job and the job's step), and
    /// an add whose parent another add of the batch made is written inside it.
    /// </summary>
    /// <remarks>
    /// <b>Every change names the elements of the reading as it was before any of them</b>, or one an add of the
    /// batch made, by the id that add gave, as <c>GhgBody.Batch</c> plans them. A change refused is left out and
    /// the others are applied.
    /// </remarks>
    /// <returns>The one edit applied, empty when nothing changed.</returns>
    public SpliceEdit Batch(Action writes)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (_batch is not null) throw new InvalidOperationException("A batch is already open on this body.");

        // The library's own sentence for a body nothing is written to, as every change of a batch is refused with.
        var readOnly = _body.Plan(new ModelChange.Save()) as PlanResult.Refused;
        _batch = new PipelinePlanner(_body.Bytes, Reading.Elements, readOnly?.Reason);
        PipelinePlanner planned;
        try
        {
            writes();
        }
        finally
        {
            planned = _batch;
            _batch = null;
        }

        var edit = new SpliceEdit(planned.Splices);
        _body.Apply(edit);
        Remember();
        return edit;
    }

    /// <summary>
    /// Plans <paramref name="changes"/> together against the body as it is now and applies them as one edit
    /// (<see cref="LastEdit"/>), or refuses them all with the first refusal and writes nothing: the write of one
    /// command, whose changes stand or fall together.
    /// </summary>
    /// <remarks>
    /// As in <see cref="Batch"/>, every change names an element of the reading as it was before any of them, or one
    /// an add of the same write made, by the id that add gave.
    /// </remarks>
    /// <returns><see cref="EditOutcome.Unchanged"/> when the changes ask for what the body already says.</returns>
    public EditOutcome Write(IReadOnlyList<ModelChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (_batch is not null) throw new InvalidOperationException("A batch is open on this body.");

        var readOnly = _body.Plan(new ModelChange.Save()) as PlanResult.Refused;
        var planner = new PipelinePlanner(_body.Bytes, Reading.Elements, readOnly?.Reason);
        foreach (var change in changes)
        {
            if (planner.Plan(change) is { } refused) return EditOutcome.Refused(refused);
        }

        var splices = planner.Splices;
        if (splices.Count == 0) return EditOutcome.Unchanged;

        var edit = new SpliceEdit(splices);
        _body.Apply(edit);
        LastEdit = edit;
        Remember();
        return EditOutcome.Applied;
    }

    /// <summary>Applies <paramref name="splices"/>, the inverse of an earlier edit, as one edit (<see cref="LastEdit"/>).</summary>
    public void Restore(IReadOnlyList<Splice> splices)
    {
        ArgumentNullException.ThrowIfNull(splices);
        var edit = new SpliceEdit(splices);
        _body.Apply(edit);
        LastEdit = edit;
        Remember();
    }

    /// <summary>
    /// The id the plugin read the stage, job, step or template reference of DISL id <paramref name="id"/> with, the
    /// first of that id in reading order, as a gesture finds it; null for an id the reading does not hold.
    /// </summary>
    /// <remarks>
    /// The definition derives its ids (a stage's name, <c>stage/job</c>, <c>job/step-n</c>), and the plugin reads
    /// each element under an id of its own place, because the derived ids are not unique where two stages share a
    /// name (finding A8). The DISL model is built from the reading's nodes in their order, so the two are paired by
    /// place.
    /// </remarks>
    public string? ReadIdOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _readIds.Of(_body.Bytes, ReadIds).GetValueOrDefault(id);
    }

    private Dictionary<string, string> ReadIds()
    {
        var read = Reading.Elements.Where(element => !element.IsRelation && element.Type != "Pipeline").ToList();
        var nodes = Disl.Diagram.Nodes;
        if (nodes.Count < read.Count) throw new InvalidOperationException("The DISL model of this pipeline does not hold every node of its reading.");

        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < read.Count; index++)
        {
            ids.TryAdd(nodes[index].Id, read[index].Id);
        }

        return ids;
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

    /// <summary>Finds this body again from the model its last reading gave.</summary>
    private void Remember() => _readers.AddOrUpdate(Model, this);

    /// <summary>The reader's finding that the body is not YAML, with its line and the reader's own message; null for a body that is.</summary>
    private (int Line, string Reason)? Unparseable =>
        Reading.Findings.FirstOrDefault(each => each.Code == PipelineValidator.UnparseableRuleId) is { } finding
            ? (finding.Location?.Line ?? 1, finding.Detail?.GetValueOrDefault(PipelineYamlPlugin.ReasonDetail) ?? finding.Message)
            : null;
}
