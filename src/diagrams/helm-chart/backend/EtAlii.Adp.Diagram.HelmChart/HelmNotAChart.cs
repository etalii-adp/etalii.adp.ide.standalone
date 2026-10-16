namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// A registered folder that is not a Helm chart: exactly one warning, <c>helm.not-a-chart</c>, attributed
/// to the folder itself, and no other finding; the model is empty, so the canvas shows its empty state
/// (helm-chart-disl-fbl, Requirement 5.4, finding B10).
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for gap 8 of the definition's companion</b> (<c>definition/helm-chart.md</c>, its second
/// half). FBL makes a folder that fails <c>recognise</c> an unreadable body, reported as
/// <c>std.unparseable</c>, an error; a built-in's <c>code</c> may be computed and its <c>severity</c> may not,
/// so one built-in cannot be the error <c>helm.unreadable-yaml</c> for a file and this warning for the
/// folder. It goes when DISL lets a built-in's severity be computed.
/// </para>
/// <para>
/// It reads the reader's answer, <see cref="HelmChart.IsChart"/>, until the folder is read through the
/// binding (task 9), which moves it onto the folder body's unreadable answer.
/// </para>
/// </remarks>
internal static class HelmNotAChart
{
    /// <summary>The tool's code for a registered folder that is not a chart.</summary>
    public const string Code = "helm.not-a-chart";

    /// <summary>What the tool says of it.</summary>
    public const string Sentence = "This folder is not a Helm chart: it has no Chart.yaml.";

    /// <summary>
    /// The one finding of a registered folder <paramref name="chart"/> is not a chart of, at the folder
    /// (<c>.</c>, which core keeps fresh as a folder-located problem); null for a chart.
    /// </summary>
    public static IReadOnlyList<DiagramProblem>? Findings(HelmChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return chart.IsChart
            ? null
            : [new DiagramProblem(DiagramProblemSeverity.Warning, Sentence, Code, new DiagramProblemFileLocation("."))];
    }
}
