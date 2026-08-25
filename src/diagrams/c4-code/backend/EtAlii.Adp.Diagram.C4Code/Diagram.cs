namespace EtAlii.Adp.Diagram.C4Code;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/code`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("c4", "code"),
            "Code (optional)",
            "The classes and their relations inside one component, at the level source code is written."),
    ];

    // Deliberately no extension, unlike its six siblings. The other C4 types keep their
    // model in a Structurizr DSL document; the code level does not, because the DSL declares
    // no code view and C4 specifies UML class or ER notation for it instead. Claiming .dsl
    // would also make this - the one C4 type that cannot open anything - the first claimant
    // of that extension in catalog order, which broke opening a .dsl that has no .adp beside
    // it (found by the shared-model integration test). A deviation from Requirement 2.2,
    // recorded in tasks.md.
}
