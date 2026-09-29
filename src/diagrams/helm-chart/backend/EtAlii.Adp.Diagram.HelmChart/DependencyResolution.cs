namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The three-state matcher (Requirement 5.2): declared dependencies against what actually
/// sits in <c>charts/</c>.
/// </summary>
/// <remarks>
/// <para>
/// A dependency is <b>Resolved</b> when a vendored entry matches it, <b>Unvendored</b> when
/// nothing does - a marked open end, and deliberately not a validation finding, because a
/// repository that vendors nothing and lets <c>helm dependency build</c> fetch at deploy time
/// is the common case. A vendored entry no dependency declares is <b>Undeclared</b>, which
/// validation does warn about (Requirement 10.4).
/// </para>
/// <para>
/// Matching is ordinal, against the entry's name (a directory's own name, or an archive's
/// filename with the version tail already stripped by the reader). Helm vendors under the
/// dependency's chart <em>name</em> even when an <c>alias</c> is set - the alias renames the
/// mount, not the folder - so an entry matches on the name first and the alias as well
/// (that is what "matching respects alias" means in practice). No version or range is ever
/// evaluated: the lock's pin is display data (the design refuses a range engine).
/// </para>
/// </remarks>
public static class DependencyResolution
{
    /// <summary>Matches every declaration and accounts for every vendored entry, pure over its inputs.</summary>
    public static DependencyResolutionResult Match(
        IReadOnlyList<DependencyDeclaration> dependencies,
        IReadOnlyList<VendoredEntry> vendored)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(vendored);

        var resolved = new List<ResolvedDependency>(dependencies.Count);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dependency in dependencies)
        {
            var match = vendored.FirstOrDefault(entry =>
                string.Equals(entry.EntryName, dependency.Name, StringComparison.Ordinal)
                || (dependency.Alias is { Length: > 0 } alias
                    && string.Equals(entry.EntryName, alias, StringComparison.Ordinal)));

            resolved.Add(new ResolvedDependency(dependency, match));
            if (match is not null)
            {
                claimed.Add(match.RelativePath);
            }
        }

        var undeclared = vendored
            .Where(entry => !claimed.Contains(entry.RelativePath))
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new DependencyResolutionResult(resolved, undeclared);
    }
}
