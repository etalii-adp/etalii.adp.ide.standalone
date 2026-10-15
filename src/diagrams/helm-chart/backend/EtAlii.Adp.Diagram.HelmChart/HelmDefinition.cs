using System.Runtime.CompilerServices;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The Helm chart diagram's bundled DISL definition (<c>definition/helm-chart.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, and what is derived from it: the property rows of every node and
/// relation, the sentence a write is refused with, and an element's wire type, mapped onto the host's ids
/// by its <c>x-helm</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is built from today's reading</b> by <see cref="HelmDisl"/>, once per reading and kept for
/// as long as the reading lives, so every selection of one reading reads one model. Reading the folder
/// through the binding comes later (helm-chart-disl-fbl, task 9).
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

    private static readonly ConditionalWeakTable<HelmChart, DislDiagram> Models = [];

    /// <summary>What <c>env</c> reads: nothing in this diagram can be edited.</summary>
    private static readonly DislEnv Env = new(ReadOnly: true);

    /// <summary>The definition.</summary>
    private static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-helm</c>.</summary>
    private static WireIdMap Ids => LoadedIds.Value;

    /// <summary>Why nothing here can be written: the definition's <c>std.readOnly</c>.</summary>
    public static string Refusal => LoadedRefusal.Value;

    /// <summary>The property rows of the element <paramref name="elementId"/> names in <paramref name="chart"/>: its form's; none for the empty id and for an id the chart does not hold.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(HelmChart chart, string elementId)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(elementId);
        if (elementId.Length == 0 || ModelOf(chart).ElementById(elementId) is not { } element) return [];

        return [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(Row)];
    }

    /// <summary>The wire type of the element <paramref name="elementId"/> names in <paramref name="chart"/>, as <c>x-helm</c> maps its type; null for an id the chart does not hold.</summary>
    public static string? WireTypeOf(HelmChart chart, string elementId)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(elementId);
        return ModelOf(chart).ElementById(elementId) is { } element
            ? LoadedTypePrefix.Value + Ids.Types.GetValueOrDefault(element.Type.Name, element.Type.Name)
            : null;
    }

    /// <summary><paramref name="chart"/> as the definition's model.</summary>
    private static DislDiagram ModelOf(HelmChart chart) =>
        Models.GetValue(chart, read => HelmDisl.Build(Specification, read, HelmGraph.Derive(read)));

    private static JsonElement Block(string name) => Specification.Root.GetProperty(name);

    /// <summary>A row as the grid draws it: every item here is a <c>readonly</c> widget, which is one line.</summary>
    private static ContextPropertyDefinition Row(DerivedRow row) =>
        new(row.Id, row.Label, row.Value, ContextPropertyEditor.Line, row.ReadOnlyReason, row.Group);
}
