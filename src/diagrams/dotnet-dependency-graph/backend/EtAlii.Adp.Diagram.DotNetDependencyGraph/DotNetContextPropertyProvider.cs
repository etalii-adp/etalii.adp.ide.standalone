using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// What the property grid shows for a selected element of a .NET dependency graph -
/// everything, and none of it editable (Requirements 4 and 5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row carries a non-empty <see cref="ContextPropertyDefinition.ReadOnlyReason"/></b>,
/// following the house shape the mindmap's two read-only rows established and
/// <c>AnsibleContextPropertyProvider</c> generalised: cause first, then the remedy, naming the
/// file the value actually lives in. A reader who cannot change a value here deserves to be
/// told what would have to change instead - and for this type the answer is always a file the
/// build owns rather than anything ADP can write.
/// </para>
/// <para>
/// Read-only is enforced rather than styled: <c>ContextPropertyResolver</c> refuses a write to
/// a property its owner described as read-only, server-side, whatever a client claimed.
/// <see cref="SetAsync"/> is therefore unreachable in practice, and refuses anyway - a provider
/// that would accept a write if the resolver ever changed is a trap rather than a design.
/// </para>
/// <para>
/// <b>An absence is shown, not omitted</b> (Requirement 4.4). Where a value cannot be
/// discovered - a project whose framework names no .NET version, a package that has never been
/// restored on this machine - the row is still present and says so. This is the one place this
/// module departs from the ansible provider, which drops a row it has nothing to put in: 4.4
/// asks for the absence to be visible, and a missing row and a missing value read very
/// differently to someone wondering whether the diagram simply failed to look.
/// </para>
/// </remarks>
public sealed class DotNetContextPropertyProvider : IContextPropertyProvider
{
    /// <summary>
    /// Why nothing here can be written. Cause first, then the remedy - and the remedy is never
    /// "in ADP", because the whole subject of this diagram belongs to the build.
    /// </summary>
    private const string Refusal =
        "A .NET dependency graph shows the solution as it is and changes nothing in it. " +
        "Edit the project file this value comes from, and the diagram will follow.";

    /// <summary>What a row says when the value could not be discovered at all.</summary>
    internal const string NotDiscoverable = "Not discoverable";

    /// <summary>What a description row says when the package has never been restored here.</summary>
    internal const string NotCached = "Not available - this package is not in the local NuGet cache";

    private static readonly ValueTask<IReadOnlyList<ContextPropertyDefinition>> Empty =
        ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);

    private readonly IDependencyGraphStore _store;

    public DotNetContextPropertyProvider(IDependencyGraphStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (target.ElementId.Length == 0)
        {
            return Empty;
        }

        var graph = _store.GetOrLoad(target.ResolvedFullPath);

        var project = graph.Projects.FirstOrDefault(candidate => string.Equals(candidate.Id, target.ElementId, StringComparison.Ordinal));
        if (project is not null)
        {
            return ValueTask.FromResult(Describe(project));
        }

        var package = graph.Packages.FirstOrDefault(candidate => string.Equals(candidate.Id, target.ElementId, StringComparison.Ordinal));
        return package is null ? Empty : ValueTask.FromResult(Describe(package));
    }

    /// <summary>Refuses, always, with the reason every row already carries.</summary>
    public ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target, string propertyId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextPropertyResult.Failure(Refusal));

    /// <summary>Requirement 4.1: name, target framework, and the .NET version where available.</summary>
    private static IReadOnlyList<ContextPropertyDefinition> Describe(ProjectNode project) =>
    [
        Row("name", "Name", project.Name, project.RelativePath, "Identity"),
        Row("path", "Path", project.RelativePath, project.RelativePath, "Identity"),
        Row(
            "target-frameworks",
            project.TargetFrameworks.Count == 1 ? "Target framework" : "Target frameworks",
            project.TargetFrameworks.Count == 0 ? NotDiscoverable : string.Join(", ", project.TargetFrameworks),
            project.RelativePath,
            "Build"),
        // Present even when absent, and saying so: a project on netstandard2.0 has no .NET
        // version, and that is a fact about the project rather than a gap in the reading.
        Row(
            "dotnet-version",
            ".NET version",
            project.DotNetVersion ?? NotDiscoverable,
            project.RelativePath,
            "Build"),
    ];

    /// <summary>Requirements 4.2 and 5.1: the package's id, its version or versions, its description.</summary>
    private static IReadOnlyList<ContextPropertyDefinition> Describe(PackageNode package)
    {
        var rows = new List<ContextPropertyDefinition>
        {
            Row("package-id", "Package", package.PackageId, "the project files that reference it", "Identity"),
            Row(
                "package-version",
                package.Versions.Count > 1 ? "Versions" : "Version",
                package.Versions.Count == 0 ? NotDiscoverable : string.Join(", ", package.Versions),
                "the project files that reference it",
                "Identity"),
        };

        if (package.HasVersionConflict)
        {
            // Collapsing loudly (Requirement 3.5): the conflict is stated on the element rather
            // than left for a reader to infer from a version list with two entries in it.
            rows.Add(Row(
                "package-version-conflict",
                "Version conflict",
                "This solution's projects reference this package at more than one version.",
                "the project files that reference it",
                "Identity"));
        }

        rows.Add(new ContextPropertyDefinition(
            "dotnet.package-description",
            "Description",
            package.Description ?? NotCached,
            ContextPropertyEditor.Line,
            // A description is not edited anywhere in this workspace, so the reason names the
            // package author rather than a file - the honest remedy rather than a plausible one.
            "Published by the package's author and read from the local NuGet cache; nothing in this workspace defines it.",
            "About"));

        return rows;
    }

    private static ContextPropertyDefinition Row(string id, string label, string value, string source, string group) =>
        new(
            $"dotnet.{id}",
            label,
            value,
            ContextPropertyEditor.Line,
            $"Defined in {source}; edit it in a text editor.",
            group);
}
