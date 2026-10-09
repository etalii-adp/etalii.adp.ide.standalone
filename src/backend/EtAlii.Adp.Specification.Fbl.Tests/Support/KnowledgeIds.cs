using System.Globalization;

namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>
/// The id derivations of the Knowledge designer's specification, standing in for DESL, which this
/// library does not implement any more than it implements DISL. Each mirrors <c>persistence.ids</c>
/// of etalii-adp/etalii.adp <c>definitions/designers/knowledge.des</c>: a column and a sort by their
/// view and property, a cell by its row and property, a cell's item by its cell and what it names,
/// a group setting by its view, slot and key, and a filter's conditions and groups by their place.
/// </summary>
internal static class KnowledgeIds
{
    public static string? Derive(IdRequest request) => request.Rule switch
    {
        "table" => "table",
        "column" => $"{request.ParentId}/columns/{Attribute(request, "property")}",
        "sort" => $"{request.ParentId}/sorts/{Attribute(request, "property")}",
        "condition1" or "filterGroup1" => string.Create(CultureInfo.InvariantCulture, $"{request.ParentId}/filter/{request.PositionInSlot}"),
        "condition2" or "filterGroup2" or "condition3" => string.Create(CultureInfo.InvariantCulture, $"{request.ParentId}/{request.PositionInSlot}"),
        "groupOrder" or "hiddenGroup" or "collapsedGroup" => $"{request.ParentId}/{request.ParentSlot}/{Attribute(request, "key")}",
        "cell" => $"{request.ParentId}/{Attribute(request, "property")}",
        "cellOption" => $"{request.ParentId}/{Attribute(request, "option")}",
        "cellRow" => $"{request.ParentId}/{Attribute(request, "row")}",
        _ => null,
    };

    private static string Attribute(IdRequest request, string name) =>
        request.Attributes.TryGetValue(name, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
}
