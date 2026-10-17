using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The Helm chart diagram's bundled DISL definition (<c>definition/helm-chart.dis</c>, from
/// etalii-adp/etalii.adp) and its binding (<c>helm-chart.fbl</c>, the module's own), each loaded once, and what
/// is derived from them: the property rows of every node and relation, the findings, the relations
/// <c>Declares</c>, <c>Overrides</c> and <c>Configures</c>, the sentence a write is refused with, and an
/// element's wire type, mapped onto the host's ids by its <c>x-helm</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is built by <see cref="DislModelBuilder"/> from what the plugin delivers</b>
/// (<see cref="HelmChartFolderPlugin"/>), which computes every id again by the definition's id expressions. A
/// reading builds it once (<see cref="HelmReading.Disl"/>), so every selection, every finding and every
/// delivered relation of one reading reads one model (helm-chart-disl-fbl, task 9).
/// </para>
/// <para>
/// <b>Everything is read-only</b> (<c>env.readOnly</c>): every row carries the reason its form item gives,
/// which names the file the value is defined in, and a write is refused with the definition's
/// <c>std.readOnly</c>.
/// </para>
/// </remarks>
internal static class HelmDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(HelmDefinition).Assembly, "helm-chart.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-helm"));

    private static readonly Lazy<string> LoadedRefusal = new(() => Block("behavior").GetProperty("messages").GetProperty("std.readOnly") is { ValueKind: JsonValueKind.String } message
        ? message.GetString()!
        : throw new InvalidOperationException("The definition words no std.readOnly to refuse a write with."));

    private static readonly Lazy<string> LoadedTypePrefix = new(() => Block("x-helm").GetProperty("typePrefix") is { ValueKind: JsonValueKind.String } prefix
        ? prefix.GetString()!
        : throw new InvalidOperationException("The definition's x-helm block names no typePrefix."));

    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(HelmDefinition).Assembly, "helm-chart.fbl", "chart"));

    /// <summary>What <c>env</c> reads: nothing in this diagram can be edited.</summary>
    private static readonly DislEnv Env = new(ReadOnly: true);

    /// <summary>The definition.</summary>
    private static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-helm</c>.</summary>
    private static WireIdMap Ids => LoadedIds.Value;

    /// <summary>The binding the folder is read through: <c>helm-chart.fbl#chart</c>.</summary>
    public static FblBinding Binding => LoadedBinding.Value;

    /// <summary>Why nothing here can be written: the definition's <c>std.readOnly</c>.</summary>
    public static string Refusal => LoadedRefusal.Value;

    /// <summary>The property rows of the element <paramref name="elementId"/> names in <paramref name="diagram"/>: its form's; none for the empty id and for an id the model does not hold.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DislDiagram diagram, string elementId)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(elementId);
        if (elementId.Length == 0 || diagram.ElementById(elementId) is not { } element) return [];

        return [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(Row)];
    }

    /// <summary>
    /// The findings of <paramref name="reading"/>, located relative to the chart folder: for a registered folder
    /// that is not a chart, <see cref="HelmNotAChart"/>'s one warning; otherwise the definition's, as
    /// <see cref="ConstraintEvaluator"/> derives them with the reading's own findings (a file that did not parse
    /// or could not be opened is <c>std.unparseable</c>) as the reader's, ten of the tool's eleven codes
    /// (helm-chart-disl-fbl, Requirements 5.2 to 5.4), in the tool's order (<see cref="HelmFindingOrder"/>).
    /// </summary>
    /// <remarks>
    /// Only the reading's findings are handed to the evaluator, and only the evaluator's are mapped. The model's
    /// own (<see cref="DislModelBuilder.Complete"/>'s <c>std.duplicateId</c> for two dependencies of one name, and
    /// <c>std.derivedId</c> for the relations such a pair repeats) are not, so no finding appears that the tool
    /// does not give today (Q4).
    /// </remarks>
    public static IReadOnlyList<DiagramProblem> Findings(HelmReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (HelmNotAChart.Findings(reading) is { } notAChart) return notAChart;

        var model = reading.Disl.Diagram;
        var findings = ConstraintEvaluator.Evaluate(Specification, model, new DislConstraintOptions(Env, [.. reading.Folder.Findings.Select(ReaderFinding)]));
        return [.. HelmFindingOrder.Of(model, findings).Select(Problem)];
    }

    /// <summary>The wire type of the element <paramref name="elementId"/> names in <paramref name="diagram"/>, as <c>x-helm</c> maps its type; null for an id the model does not hold.</summary>
    public static string? WireTypeOf(DislDiagram diagram, string elementId)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(elementId);
        return diagram.ElementById(elementId) is { } element
            ? LoadedTypePrefix.Value + Ids.Types.GetValueOrDefault(element.Type.Name, element.Type.Name)
            : null;
    }

    /// <summary>The model of a reading through the binding, with the three relations that always have both ends derived.</summary>
    public static DislModel Model(FblModel reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return DislModelBuilder.From(reading, Specification);
    }

    private static JsonElement Block(string name) => Specification.Root.GetProperty(name);

    /// <summary>
    /// A finding of the reading as the reader's finding of its code: its message is the reason, which is the
    /// plugin's whole sentence and the built-in gives as it is, and a line of 0, which says the parser gave none,
    /// is no line.
    /// </summary>
    private static DislReaderFinding ReaderFinding(Finding finding)
    {
        var detail = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, string value) in finding.Detail ?? new Dictionary<string, string>())
        {
            detail[key] = value;
        }

        detail["reason"] = finding.Message;
        var line = finding.Location is { Line: > 0 } located ? located.Line : (int?)null;
        return new DislReaderFinding(finding.Code, detail, line, finding.Location?.File is { Length: > 0 } file ? file : null);
    }

    /// <summary>A finding as the host's problem, at its file and line; a finding without a line is at line 0, as the tool reports it.</summary>
    private static DiagramProblem Problem(DislFinding finding) => new(
        finding.Severity switch
        {
            "error" => DiagramProblemSeverity.Error,
            "warning" => DiagramProblemSeverity.Warning,
            _ => DiagramProblemSeverity.Info,
        },
        finding.Message,
        finding.Code,
        finding.File is { } file ? new DiagramProblemFileLocation(file, finding.Line is { } line ? (uint)line : 0) : null);

    /// <summary>A row as the grid draws it: every item here is a <c>readonly</c> widget, which is one line.</summary>
    private static ContextPropertyDefinition Row(DerivedRow row) =>
        new(row.Id, row.Label, row.Value, ContextPropertyEditor.Line, row.ReadOnlyReason, row.Group);
}
