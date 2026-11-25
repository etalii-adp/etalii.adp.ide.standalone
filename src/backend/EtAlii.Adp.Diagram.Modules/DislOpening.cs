using System.Text;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Links;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// Load-time validation of a document of a DISL diagram type that keeps a document id (FBL 0.23 §17.4, etalii.adp spec 092,
/// R6): a document id where it has none, and each link written again whose parts differ from what resolution found, with
/// the cached name of a linked element. What it finds is held pending, written with the document's first edit and never on
/// opening it (<see cref="DocumentEntry{TBody, TModel}.Pending"/>).
/// </summary>
public static class DislOpening
{
    /// <summary>
    /// The text of <paramref name="body"/> with what opening it finds, or null when it finds nothing to change: a type without
    /// a document id, a read-only or unreadable body, and a document whose id and links are as resolution finds them.
    /// <paramref name="body"/> itself is changed, so the caller hands a body of its own.
    /// </summary>
    /// <param name="body">The document's body, read through its binding.</param>
    /// <param name="specification">Its type's specification.</param>
    /// <param name="diagram">The diagram <paramref name="body"/> holds, as its module builds it under <paramref name="specification"/>.</param>
    /// <param name="workspace">The workspace the document's links resolve in; null when none is known, and links are then left as they are.</param>
    public static string? Pending(OpenBody body, DislSpecification specification, DislDiagram diagram, LinkWorkspace? workspace)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(diagram);
        if (!specification.HasDocumentId || body.IsReadOnly || body.Model.Unreadable) return null;

        var before = body.Bytes;
        if (body.Model.DocumentId is null && DislIds.NewDocumentId(specification) is { } id) body.Change(new ModelChange.SetDocumentId(id));
        if (workspace is not null)
        {
            foreach (var refresh in Refreshes(diagram, workspace))
            {
                // A binding that keeps no label for the linked element names it by the link alone: the link is written then.
                if (body.Change(DislWrite.ToFbl(specification, refresh)) is PlanResult.Refused && refresh.Attributes.Count > 1)
                {
                    body.Change(DislWrite.ToFbl(specification, refresh with { Attributes = refresh.Attributes.Where(pair => specification.Metamodel.TypeOf(refresh.Type)?.Attributes.GetValueOrDefault(pair.Key)?.Type == "link").ToDictionary(StringComparer.Ordinal) }));
                }
            }
        }
        return body.Bytes.AsSpan().SequenceEqual(before) ? null : Encoding.UTF8.GetString(body.Bytes);
    }

    /// <summary>The sets that write each link of <paramref name="diagram"/> as resolution in <paramref name="workspace"/> finds it, with a linked element's cached name.</summary>
    private static IEnumerable<DislChange.Set> Refreshes(DislDiagram diagram, LinkWorkspace workspace)
    {
        foreach (var element in diagram.Elements)
        {
            if (element.IsDerived) continue;
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var attribute in element.Type.Attributes.Values.Where(attribute => attribute.Type == "link"))
            {
                var value = element.ValueOf(attribute.Name);
                if (value is string text && Refreshed(workspace, text) is { } resolution)
                {
                    if (resolution.Refreshed!.Text != text) attributes[attribute.Name] = resolution.Refreshed.Text;
                    // A linked element that keeps its label caches the target's name in it; one that keeps none is named by the link.
                    if (element.Type.LinkedAttribute == attribute.Name && element.Type.LabelAttribute is { } label && label != attribute.Name
                        && resolution.Refreshed.Name is { } name && element.ValueOf(label) is string cached && cached.Length > 0 && cached != name)
                    {
                        attributes[label] = name;
                    }
                }
                else if (value is IEnumerable<object?> items)
                {
                    var texts = items.OfType<string>().ToList();
                    var written = texts.Select(item => Refreshed(workspace, item)?.Refreshed!.Text ?? item).ToList();
                    if (!written.SequenceEqual(texts)) attributes[attribute.Name] = written;
                }
            }
            if (attributes.Count > 0) yield return new DislChange.Set(element.Id, element.Type.Name, attributes);
        }
    }

    private static LinkResolution? Refreshed(LinkWorkspace workspace, string text) =>
        text.Length > 0 && workspace.Resolve(text) is { Resolved: true, Refreshed: not null } resolution ? resolution : null;
}
