using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.FolderWithExtension;

/// <summary>
/// A good definition and a self-contradicting one in the same array: a folder-subject type
/// that also declares a sibling extension. A folder has no sibling body, so the two claims
/// cannot both be true, and believing either half would make the type behave differently
/// depending on whether routing or validation was asking. The good one must survive; only the
/// contradiction costs an entry.
/// </summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(new DiagramOrigin("fixture", "survivor"), "Survivor"),
        new(
            new DiagramOrigin("fixture", "contradiction"),
            "Contradiction",
            Extension: ".yml",
            Subject: DiagramSubject.Folder),
    ];
}
