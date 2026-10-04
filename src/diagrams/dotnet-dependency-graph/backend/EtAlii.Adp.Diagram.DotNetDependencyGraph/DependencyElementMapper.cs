using EtAlii.Adp.Hierarchy;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Turns a derived graph and its layout into the core element and delta vocabulary - without
/// extending that vocabulary by a single field.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stored positions win element by element</b> (Requirement 6.3). A solution that has gained
/// a project keeps every arrangement the user made and computes a position only for the new
/// one, rather than losing the whole layout because one id is unknown. A stored id matching no
/// element is simply not applied (Requirement 6.4); core's <c>RegistrationLayout</c> drops it
/// on the next write.
/// </para>
/// <para>
/// <b>Two of the four delta kinds are never emitted.</b> Nothing on this diagram folds, so no
/// group or ungroup delta exists; nothing on it is authored, so an element appears, disappears,
/// or is replaced because the solution changed.
/// </para>
/// </remarks>
public sealed class DependencyElementMapper
{
    /// <summary>The mime-style element kinds this type puts on the wire.</summary>
    public const string ProjectType = "dotnet/dependency-graph+project";
    public const string PackageType = "dotnet/dependency-graph+package";
    public const string EdgeType = "dotnet/dependency-graph+edge";

    private static readonly string PayloadTypeUrl =
        $"type.googleapis.com/{Wire.DependencyElementPayload.Descriptor.FullName}";

    /// <summary>Every element of the graph, arranged - stored positions overlaying computed ones.</summary>
    public IReadOnlyList<DiagramElement> Elements(
        DependencyGraphModel graph,
        IReadOnlyDictionary<string, RegistrationPosition>? stored = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var computed = DotNetDependencyGraphLayout.Compute(graph);
        var elements = new List<DiagramElement>(graph.Projects.Count + graph.Packages.Count + graph.Edges.Count);

        foreach (var project in graph.Projects)
        {
            (double x, double y) = PositionOf(project.Id, computed, stored);
            elements.Add(new DiagramElement(
                project.Id,
                x,
                y,
                ProjectType,
                PayloadTypeUrl,
                Payload(new Wire.DependencyElementPayload
                {
                    Name = project.Name,
                    Kind = Wire.DependencyElementKind.Project,
                    ProjectRelativePath = { project.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries) },
                    TargetFrameworks = { project.TargetFrameworks },
                })));
        }

        foreach (var package in graph.Packages)
        {
            (double x, double y) = PositionOf(package.Id, computed, stored);
            elements.Add(new DiagramElement(
                package.Id,
                x,
                y,
                PackageType,
                PayloadTypeUrl,
                Payload(new Wire.DependencyElementPayload
                {
                    Name = package.PackageId,
                    Kind = Wire.DependencyElementKind.Package,
                    Versions = { package.Versions },
                    HasVersionConflict = package.HasVersionConflict,
                    DependentProjectCount = package.DependentProjectCount,
                    IsAmbient = package.IsAmbient,
                })));
        }

        foreach (var edge in graph.Edges)
        {
            // An edge has no position of its own - it follows its endpoints - so it carries the
            // origin and the canvas draws it between the two boxes it names.
            elements.Add(new DiagramElement(
                edge.Id,
                0,
                0,
                EdgeType,
                PayloadTypeUrl,
                Payload(new Wire.DependencyElementPayload
                {
                    Name = edge.Id,
                    Kind = edge.Kind == DependsOnKind.Project
                        ? Wire.DependencyElementKind.ProjectReference
                        : Wire.DependencyElementKind.PackageReference,
                })));
        }

        return elements;
    }

    /// <summary>The stored position where there is one, the computed one otherwise.</summary>
    private static (double X, double Y) PositionOf(
        string id,
        IReadOnlyDictionary<string, (double X, double Y)> computed,
        IReadOnlyDictionary<string, RegistrationPosition>? stored)
    {
        if (stored is not null && stored.TryGetValue(id, out var authored))
        {
            return (authored.X, authored.Y);
        }

        return computed.TryGetValue(id, out var position) ? position : (0, 0);
    }

    private static ReadOnlyMemory<byte> Payload(Wire.DependencyElementPayload payload) => payload.ToByteArray();
}
