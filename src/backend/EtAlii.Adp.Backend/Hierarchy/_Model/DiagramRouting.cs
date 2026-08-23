using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>What <see cref="DiagramFileRouter"/> decided about a file.</summary>
public abstract record DiagramRouting
{
    /// <summary>
    /// The file is a diagram of <paramref name="Definition"/>'s type. <paramref name="BodyPath"/>
    /// is where its document lives - the registration file itself for a type with no sibling,
    /// whether or not that file exists yet. <paramref name="RegistrationPath"/> is null for a
    /// body opened without one (Requirement 2.7), which is never created implicitly.
    /// </summary>
    public sealed record Routed(DiagramDefinition Definition, string? RegistrationPath, string BodyPath) : DiagramRouting;

    /// <summary>A registration file naming a MIME type no discovered definition matches (Requirement 2.6).</summary>
    public sealed record UnknownType(string Path, string MimeType) : DiagramRouting;

    /// <summary>A body file whose extension more than one type declares (Requirement 2.8).</summary>
    public sealed record Ambiguous(string Path, string Extension, IReadOnlyList<DiagramDefinition> Claimants) : DiagramRouting;

    /// <summary>A registration file that could not be read.</summary>
    public sealed record Unreadable(string Path) : DiagramRouting;

    /// <summary>Not a registration file, and no type claims its extension.</summary>
    public sealed record NotADiagram(string Path) : DiagramRouting;
}
