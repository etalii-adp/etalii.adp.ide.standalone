namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// What <see cref="TemplateScan"/> found literally written in one template file: every list
/// sorted ordinally and distinct, so two scans of the same text are the same facts.
/// </summary>
/// <param name="Kinds">Values of column-zero <c>kind:</c> lines - one file may render several documents.</param>
/// <param name="ApiVersions">Values of column-zero <c>apiVersion:</c> lines.</param>
/// <param name="Defines">Names defined by <c>{{ define "name" }}</c> blocks (a partial's exports).</param>
/// <param name="References">Names referenced by <c>{{ include "name" ... }}</c> or <c>{{ template "name" ... }}</c> with a literal name.</param>
public sealed record TemplateFacts(
    IReadOnlyList<string> Kinds,
    IReadOnlyList<string> ApiVersions,
    IReadOnlyList<string> Defines,
    IReadOnlyList<string> References);
