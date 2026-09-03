using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A small but genuine Helm chart, written into a temp folder for the integration flows.
/// </summary>
/// <remarks>
/// Written here rather than copied from the module's own <c>Fixtures/</c>: test projects do
/// not reference one another, and the flows need only enough of a chart to prove that core
/// routes a folder-subject registration and streams what the module makes of it.
/// </remarks>
internal static class HelmFixture
{
    public static void CopyTo(string folder)
    {
        Write(folder, "Chart.yaml", ChartYaml(version: "2.1.0"));
        Write(folder, "values.yaml",
            "global:\n  imageRegistry: registry.example.com\ncache:\n  enabled: true\nreplicaCount: 2\n");
        Write(folder, "Chart.lock",
            "dependencies:\n- name: redis\n  repository: https://charts.example.com\n  version: 17.3.2\n"
            + "- name: postgres\n  repository: oci://registry.example.com/charts\n  version: 12.1.0\n"
            + "digest: sha256:0000000000000000000000000000000000000000000000000000000000000000\n"
            + "generated: \"2026-01-01T00:00:00Z\"\n");
        Write(folder, IoPath.Combine("templates", "deployment.yaml"),
            "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: {{ include \"web-shop.fullname\" . }}\n");
        Write(folder, IoPath.Combine("templates", "_helpers.tpl"),
            "{{- define \"web-shop.fullname\" -}}\n{{ .Release.Name }}-web-shop\n{{- end }}\n");
        Write(folder, IoPath.Combine("templates", "NOTES.txt"), "Thank you for installing {{ .Chart.Name }}.\n");
        Write(folder, IoPath.Combine("charts", "redis", "Chart.yaml"), "apiVersion: v2\nname: redis\nversion: 17.3.2\n");
        Write(folder, IoPath.Combine("charts", "redis", "templates", "statefulset.yaml"), "kind: StatefulSet\napiVersion: apps/v1\n");
    }

    /// <summary>
    /// The same chart with one mistake in it: no <c>version</c>. Enough for the validation
    /// flow to have exactly one thing to find, attributed to <c>Chart.yaml</c>.
    /// </summary>
    public static void CopyBrokenTo(string folder)
    {
        CopyTo(folder);
        Write(folder, "Chart.yaml", ChartYaml(version: null));
    }

    /// <summary>Repairs the mistake <see cref="CopyBrokenTo"/> made, as a user editing the file would.</summary>
    public static void RepairIn(string folder) =>
        Write(folder, "Chart.yaml", ChartYaml(version: "2.1.0"));

    /// <summary>
    /// One shape for both states, dependencies kept either way - dropping them with the
    /// version would add lock-drift findings and the flow wants exactly one thing to find.
    /// </summary>
    private static string ChartYaml(string? version) =>
        "apiVersion: v2\nname: web-shop\n"
        + (version is null ? "" : $"version: {version}\n")
        + "appVersion: \"5.0\"\n"
        + "description: The web shop chart the flows exercise.\n"
        + "dependencies:\n"
        + "  - name: redis\n    version: \">=17.0.0\"\n    repository: https://charts.example.com\n"
        + "    condition: cache.enabled\n    alias: cache\n"
        + "  - name: postgres\n    version: 12.1.0\n    repository: oci://registry.example.com/charts\n";

    private static void Write(string folder, string relativePath, string content)
    {
        var path = IoPath.Combine(folder, relativePath);
        Directory.CreateDirectory(IoPath.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
