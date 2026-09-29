using System.Text.RegularExpressions;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The structural judgements of Requirement 10, as a pure function over the model: a chart in,
/// findings out, no filesystem and no clock - so the diagram and the panel can never disagree.
/// </summary>
/// <remarks>
/// <para>
/// <b>The noise guards are part of the specification.</b> An Unvendored dependency is never a
/// finding - a repository that vendors nothing and lets <c>helm dependency build</c> fetch at
/// deploy time is the common case (R5.2). A missing lock is never a finding either: the lock
/// appears when someone first runs <c>helm dependency update</c>, and its absence before that
/// is a state, not a mistake (R10.5). Library charts escape the empty-templates rule, because
/// rendering nothing is what a library chart is for (R1.2).
/// </para>
/// <para>
/// The panel knows two severities, so R10.7's "informational" legacy finding is a Warning by
/// design note - the lower of the two tiers, worded as information.
/// </para>
/// </remarks>
public static class HelmRuleSet
{
    /// <summary>MAJOR.MINOR.PATCH with optional pre-release and build tails - SemVer's shape, not its algebra.</summary>
    private static readonly Regex _semVer = new(
        @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<DiagramProblem> Judge(HelmChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        if (!chart.IsChart)
        {
            // Exactly one finding, attributed to the folder itself: a registered non-chart is
            // one fact, not a cascade (R10.1). Core's ProblemStamp keeps a folder-located
            // problem fresh - the seam built for exactly this kind of rule.
            return
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    "This folder is not a Helm chart: it has no Chart.yaml.",
                    HelmRules.NotAChart,
                    new DiagramProblemFileLocation(".")),
            ];
        }

        var problems = new List<DiagramProblem>();
        problems.AddRange(UnreadableFiles(chart));
        problems.AddRange(Metadata(chart));
        problems.AddRange(Vendoring(chart));
        problems.AddRange(LockDrift(chart));
        problems.AddRange(Templates(chart));
        problems.AddRange(Conditions(chart));
        problems.AddRange(Collisions(chart));

        return problems;
    }

    /// <summary>Chart-owned YAML that would not parse, in the parser's own words (R10.2, R10.3).</summary>
    private static IEnumerable<DiagramProblem> UnreadableFiles(HelmChart chart)
    {
        if (chart.MetadataFailure is { } metadata)
        {
            yield return Unreadable(DiagramProblemSeverity.Error, metadata);
        }

        if (chart.DependenciesFailure is { } dependencies)
        {
            yield return Unreadable(DiagramProblemSeverity.Error, dependencies);
        }

        foreach (var values in chart.Values.Where(values => values.Failure is not null))
        {
            yield return Unreadable(DiagramProblemSeverity.Error, values.Failure!);
        }

        if (chart.Lock?.Failure is { } chartLock)
        {
            yield return Unreadable(DiagramProblemSeverity.Error, chartLock);
        }

        foreach (var crd in chart.Crds?.Failures ?? [])
        {
            yield return Unreadable(DiagramProblemSeverity.Error, crd);
        }

        static DiagramProblem Unreadable(DiagramProblemSeverity severity, HelmYamlFailure failure) => new(
            severity,
            $"'{failure.RelativePath}' is not readable YAML: {failure.Message}",
            HelmRules.UnreadableYaml,
            new DiagramProblemFileLocation(failure.RelativePath, failure.Line));
    }

    /// <summary>The fields Helm itself requires, and the shape it requires of one of them (R10.2, R10.7).</summary>
    private static IEnumerable<DiagramProblem> Metadata(HelmChart chart)
    {
        if (chart.MetadataFailure is not null)
        {
            // Unreadable is already reported; guessing at what an unparseable file lacks
            // would stack noise on top of the real message.
            yield break;
        }

        var line = chart.Metadata?.Line ?? 0;
        if (chart.Metadata?.Name is not { Length: > 0 })
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Error,
                "Chart.yaml declares no name; Helm requires one.",
                HelmRules.MissingName,
                new DiagramProblemFileLocation("Chart.yaml", line));
        }

        if (chart.Metadata?.Version is not { Length: > 0 })
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Error,
                "Chart.yaml declares no version; Helm requires one.",
                HelmRules.MissingVersion,
                new DiagramProblemFileLocation("Chart.yaml", line));
        }
        else if (!_semVer.IsMatch(chart.Metadata.Version))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"'{chart.Metadata.Version}' is not a SemVer version; Helm expects MAJOR.MINOR.PATCH.",
                HelmRules.VersionNotSemVer,
                new DiagramProblemFileLocation("Chart.yaml", chart.Metadata.VersionLine));
        }

        if (chart.Legacy)
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                "This is a legacy Helm 2 chart (apiVersion: v1); dependencies live in requirements.yaml.",
                HelmRules.LegacyChart,
                new DiagramProblemFileLocation("Chart.yaml", line));
        }
    }

    /// <summary>
    /// Undeclared vendored content (R10.4). The Unvendored state deliberately never fires:
    /// the prometheus example would open red otherwise, and unvendored is the common case.
    /// </summary>
    private static IEnumerable<DiagramProblem> Vendoring(HelmChart chart) =>
        DependencyResolution.Match(chart.Dependencies, chart.Vendored).Undeclared
            .Select(entry => new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"'{entry.RelativePath}' sits in charts/ but no dependency declares it.",
                HelmRules.UndeclaredVendored,
                new DiagramProblemFileLocation(entry.RelativePath)));

    /// <summary>Lock drift, both directions - only when a lock exists at all (R10.5).</summary>
    private static IEnumerable<DiagramProblem> LockDrift(HelmChart chart)
    {
        if (chart.Lock is not { Failure: null } chartLock)
        {
            yield break;
        }

        var declared = chart.Dependencies.Select(dependency => dependency.Name)
            .ToHashSet(StringComparer.Ordinal);
        var locked = chartLock.Entries.Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in declared.Where(name => !locked.Contains(name)).Order(StringComparer.Ordinal))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The dependency '{name}' is declared but not in {chartLock.RelativePath}; run helm dependency update.",
                HelmRules.LockDrift,
                new DiagramProblemFileLocation(chartLock.RelativePath));
        }

        foreach (var entry in chartLock.Entries.Where(entry => !declared.Contains(entry.Name)))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"{chartLock.RelativePath} pins '{entry.Name}', which no dependency declares; the lock is stale.",
                HelmRules.LockDrift,
                new DiagramProblemFileLocation(chartLock.RelativePath, entry.Line));
        }
    }

    /// <summary>An application chart that renders nothing (R10.6); library charts are exempt (R1.2).</summary>
    private static IEnumerable<DiagramProblem> Templates(HelmChart chart)
    {
        if (chart.Metadata is null || chart.Metadata.IsLibrary || chart.Templates.Count > 0)
        {
            yield break;
        }

        yield return new DiagramProblem(
            DiagramProblemSeverity.Warning,
            "This application chart has no templates; helm install would render nothing.",
            HelmRules.EmptyTemplates,
            new DiagramProblemFileLocation("Chart.yaml", chart.Metadata.Line));
    }

    /// <summary>A condition switching a subchart off a path the default values do not carry (R10.6).</summary>
    private static IEnumerable<DiagramProblem> Conditions(HelmChart chart)
    {
        var declaringFile = chart.Legacy ? "requirements.yaml" : "Chart.yaml";
        foreach (var dependency in chart.Dependencies.Where(dependency => dependency.State == ConditionState.Missing))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The condition '{dependency.Condition}' of dependency '{dependency.EffectiveName}' names a path that is not in values.yaml.",
                HelmRules.ConditionMissing,
                new DiagramProblemFileLocation(declaringFile, dependency.Line));
        }
    }

    /// <summary>Two dependencies mounting under one effective name cannot coexist (R10.6).</summary>
    private static IEnumerable<DiagramProblem> Collisions(HelmChart chart)
    {
        var declaringFile = chart.Legacy ? "requirements.yaml" : "Chart.yaml";
        foreach (var group in chart.Dependencies
                     .GroupBy(dependency => dependency.EffectiveName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var second = group.Skip(1).First();
            yield return new DiagramProblem(
                DiagramProblemSeverity.Error,
                $"Two dependencies resolve to the effective name '{group.Key}'; give one an alias.",
                HelmRules.NameCollision,
                new DiagramProblemFileLocation(declaringFile, second.Line));
        }
    }
}
