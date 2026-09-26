namespace EtAlii.Adp.Repository.Tests;

/// <summary>
/// One package as a manifest states it, for <see cref="DependencyInventoryTests"/>: the version
/// specifier (or every distinct specifier, sorted and comma-joined, when the client manifests
/// disagree) and which manifests said so - the latter so a failure message can name its source.
/// </summary>
internal sealed record DependencyManifestEntry(string Specifier, IReadOnlyList<string> Sources)
{
    /// <summary>The sources as one readable list for a failure message.</summary>
    public string SourceList => string.Join(", ", Sources);
}
