using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Reads what the metamodel derives (DISL §4.3, §4.9, §4.11) over the programs the loader compiled: each derived
/// attribute's program, each attribute's own <c>readOnlyReasons</c>, each derived node type's <c>derived</c> as a
/// <see cref="DislDerivedNode"/> and each derived relation type's as a <see cref="DislDerivedRelation"/>.
/// </summary>
/// <remarks>
/// A position is given the program compiled at exactly its pointer, as the model looked it up when it read the JSON
/// as it ran: an expression written as an object's <c>cel</c>, or a value that is not an Expression, has none there,
/// and still reads as unset or fails as an item.
/// </remarks>
internal static class DislDerivedReader
{
    public static void Read(DislSpecification specification)
    {
        var metamodel = specification.Metamodel;
        foreach (var attribute in metamodel.DiagramAttributes.Values.Where(attribute => attribute.IsDerived))
        {
            attribute.DerivedProgram = ProgramAt(specification, JsonPointer.Append(JsonPointer.Append("/metamodel/diagram/attributes", attribute.Name), "derived"));
        }
        foreach (var type in metamodel.Types.Values.Concat(metamodel.Relations.Values))
        {
            foreach (var attribute in type.Attributes.Values)
            {
                var declaredIn = metamodel.Types.ContainsKey(attribute.DeclaredBy) ? "/metamodel/types" : "/metamodel/relations";
                var at = JsonPointer.Append(JsonPointer.Append(JsonPointer.Append(declaredIn, attribute.DeclaredBy), "attributes"), attribute.Name);
                if (attribute.IsDerived) attribute.DerivedProgram = ProgramAt(specification, JsonPointer.Append(at, "derived"));
                if (attribute.Json.ValueKind == JsonValueKind.Object && attribute.Json.TryGetProperty("readOnlyReasons", out var reasons))
                {
                    attribute.ReadOnlyReasons = DislFieldReader.Reasons(specification, reasons, JsonPointer.Append(at, "readOnlyReasons"));
                }
                if (attribute.Json.ValueKind == JsonValueKind.Object && attribute.Json.TryGetProperty("outOfRange", out var outOfRange))
                {
                    attribute.OutOfRangeMessage = DislFieldReader.OptionalMessage(specification, outOfRange, JsonPointer.Append(at, "outOfRange"), "message");
                }
            }
        }
        foreach (var type in metamodel.Types.Values)
        {
            type.DerivedNode = Node(specification, type);
        }
        foreach (var relation in metamodel.Relations.Values)
        {
            relation.DerivedRelation = Relation(specification, relation);
        }
    }

    private static DislDerivedNode? Node(DislSpecification specification, DislType type)
    {
        if (type.Derived is not { ValueKind: JsonValueKind.Object } derived) return null;
        var pointer = JsonPointer.Append(JsonPointer.Append("/metamodel/types", type.Name), "derived");
        return new DislDerivedNode(
            Field(specification, JsonPointer.Append(pointer, "from")),
            Optional(specification, derived, pointer, "key"),
            Field(specification, JsonPointer.Append(pointer, "id")),
            Optional(specification, derived, pointer, "parent"),
            Optional(specification, derived, pointer, "slot"),
            Optional(specification, derived, pointer, "sources"),
            [.. DislJson.Members(derived, "attributes").Select(attribute => (attribute.Name, Field(specification, JsonPointer.Append(JsonPointer.Append(pointer, "attributes"), attribute.Name))))]);
    }

    private static DislDerivedRelation? Relation(DislSpecification specification, DislType relation)
    {
        var pointer = JsonPointer.Append(JsonPointer.Append("/metamodel/relations", relation.Name), "derived");
        return relation.Derived switch
        {
            { ValueKind: JsonValueKind.String } => new DislDerivedRelation.Listed(Field(specification, pointer)),
            { ValueKind: JsonValueKind.Object } derived => new DislDerivedRelation.Mapped(
                Field(specification, JsonPointer.Append(pointer, "from")),
                Optional(specification, derived, pointer, "key"),
                Field(specification, JsonPointer.Append(pointer, "source")),
                Field(specification, JsonPointer.Append(pointer, "target")),
                Optional(specification, derived, pointer, "id"),
                Optional(specification, derived, pointer, "sources"),
                Optional(specification, derived, pointer, "owner"),
                [.. DislJson.Members(derived, "attributes").Select(attribute => (attribute.Name, Field(specification, JsonPointer.Append(JsonPointer.Append(pointer, "attributes"), attribute.Name))))]),
            _ => null,
        };
    }

    /// <summary>The member <paramref name="name"/> of <paramref name="derived"/> when it states one, whatever it holds; null otherwise.</summary>
    private static DislDerivedField? Optional(DislSpecification specification, JsonElement derived, string pointer, string name) =>
        derived.TryGetProperty(name, out _) ? Field(specification, JsonPointer.Append(pointer, name)) : null;

    private static DislDerivedField Field(DislSpecification specification, string pointer) => new(pointer, ProgramAt(specification, pointer));

    private static Cel.CelProgram? ProgramAt(DislSpecification specification, string pointer) => specification.ExpressionAt(pointer)?.Program;
}
