using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// The .NET dependency graph's bundled DISL definition (<c>definition/dotnet-dependency-graph.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, and what is derived from it: the property rows of a project or a
/// package, and the sentence a write is refused with, mapped onto the host's ids by its <c>x-dotnet</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rows read the model the shared builder built from the plugin's reading.</b> A solution is read through
/// the module's binding (<c>dotnet-dependency-graph.fbl</c>, which the definition's <c>persistence.binding</c>
/// names) by <see cref="DotNetSolutionPlugin"/>, and <see cref="DotNetBody.Disl"/> is what
/// <see cref="DislModelBuilder"/> builds from that reading, once per body, so every selection of one reading reads
/// one model. No model is built by hand here: the parity tests hold the builder's model to a hand-built one, which
/// lives in the test project as their oracle.
/// </para>
/// <para>
/// <b>The summaries are the readers'</b>: a package's versions, conflict, dependent count and ambient
/// marking and a project's .NET version are computed by <see cref="DependencyGraph"/> and stored on the
/// model as the read-only attributes the definition declares them to be, never recomputed in CEL.
/// </para>
/// <para>
/// <b>Everything is read-only</b> (<c>env.readOnly</c>): every row carries the reason its form item gives,
/// and a write is refused with the definition's <c>std.readOnly</c>.
/// </para>
/// </remarks>
internal static class DotNetDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(DotNetDefinition).Assembly, "dotnet-dependency-graph.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-dotnet"));

    private static readonly Lazy<string> LoadedRefusal = new(RefusalOf);

    /// <summary>What <c>env</c> reads: nothing in this diagram can be edited.</summary>
    private static readonly DislEnv Env = new(ReadOnly: true);

    /// <summary>The definition.</summary>
    internal static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-dotnet</c>.</summary>
    private static WireIdMap Ids => LoadedIds.Value;

    /// <summary>Why nothing here can be written: the definition's <c>std.readOnly</c>.</summary>
    public static string Refusal => LoadedRefusal.Value;

    /// <summary>The property rows of the element <paramref name="elementId"/> names in <paramref name="body"/>'s model: its form's; none for a reference or an id the model does not hold.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DotNetBody body, string elementId)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(elementId);
        if (body.Disl.Diagram.ElementById(elementId) is not { } element) return [];

        return [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(Row)];
    }

    /// <summary>The sentence of <c>behavior.messages</c>' <c>std.readOnly</c>, as the definition writes it.</summary>
    private static string RefusalOf() =>
        Specification.Json.GetProperty("behavior").GetProperty("messages").GetProperty("std.readOnly") is { ValueKind: JsonValueKind.String } message
            ? message.GetString()!
            : throw new InvalidOperationException("The definition words no std.readOnly to refuse a write with.");

    /// <summary>A row as the grid draws it: every item here is a <c>readonly</c> widget, which is one line.</summary>
    private static ContextPropertyDefinition Row(DerivedRow row) =>
        new(row.Id, row.Label, row.Value, ContextPropertyEditor.Line, row.ReadOnlyReason, row.Group);
}
