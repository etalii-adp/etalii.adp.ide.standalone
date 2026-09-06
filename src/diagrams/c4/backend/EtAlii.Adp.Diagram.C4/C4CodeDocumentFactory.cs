using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The <c>c4/code</c> type's document factory, which refuses. It exists so the type is
/// registered like every other - core's startup check requires a factory for any type
/// declaring a document extension - while creating one says plainly why it cannot.
/// </summary>
/// <remarks>
/// C4 advises against hand-drawing the code level at all: "most IDEs can generate this level
/// of detail on demand". Where a code diagram is wanted, C4 specifies UML class or ER
/// notation, not the box notation the other six views use - so <c>c4/code</c> delegates to a
/// class or ER diagram type rather than growing a fourth notation inside these modules. None
/// of those types exists yet, which makes this the one C4 type that cannot be completed on its
/// own (c4-diagrams Requirement 11.7).
/// </remarks>
public sealed class C4CodeDocumentFactory : IDiagramDocumentFactory
{
    /// <summary>What a caller is told, and what the canvas shows in place of a diagram.</summary>
    public const string Unavailable =
        "C4 code diagrams are not available yet: this level uses UML class or entity-relationship " +
        "notation, which ADP does not implement yet, and C4 itself recommends generating it from " +
        "an IDE rather than drawing it by hand. Use a Component diagram to describe what is inside " +
        "a container.";

    public DiagramOrigin Origin { get; } = new("c4", "code");

    public string CreateEmptyDocument(string baseName) => throw new NotSupportedException(Unavailable);
}
