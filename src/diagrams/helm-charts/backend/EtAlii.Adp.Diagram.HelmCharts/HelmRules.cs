namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>Every rule's stable id, prefixed with the module's short name as core expects.</summary>
public static class HelmRules
{
    /// <summary>The registered folder holds no <c>Chart.yaml</c> at all.</summary>
    public const string NotAChart = "helm.not-a-chart";

    /// <summary>A chart-owned YAML file does not parse, reported in the parser's own words.</summary>
    public const string UnreadableYaml = "helm.unreadable-yaml";

    /// <summary><c>Chart.yaml</c> lacks the <c>name</c> Helm requires.</summary>
    public const string MissingName = "helm.missing-name";

    /// <summary><c>Chart.yaml</c> lacks the <c>version</c> Helm requires.</summary>
    public const string MissingVersion = "helm.missing-version";

    /// <summary>The declared <c>version</c> is not SemVer-shaped.</summary>
    public const string VersionNotSemVer = "helm.version-not-semver";

    /// <summary>Something sits in <c>charts/</c> that no dependency declares.</summary>
    public const string UndeclaredVendored = "helm.undeclared-vendored";

    /// <summary>The lock and the declarations disagree - the lock is stale.</summary>
    public const string LockDrift = "helm.lock-drift";

    /// <summary>An application chart with no templates renders nothing.</summary>
    public const string EmptyTemplates = "helm.empty-templates";

    /// <summary>A dependency's <c>condition</c> path is not in the default values.</summary>
    public const string ConditionMissing = "helm.condition-missing";

    /// <summary>Two dependencies mount under the same effective name.</summary>
    public const string NameCollision = "helm.name-collision";

    /// <summary>An <c>apiVersion: v1</c> chart - legacy Helm 2 format.</summary>
    public const string LegacyChart = "helm.legacy-chart";
}
