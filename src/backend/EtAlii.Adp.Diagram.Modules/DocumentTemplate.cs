using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// The new, empty document of a diagram type from its binding's template (FBL §13), with
/// <c>{newid:document}</c> replaced by a document id that the type's <c>persistence.documentId</c>
/// makes (DISL 0.12, FBL 17.2), so that a new document can be linked to from its first save.
/// </summary>
public static class DocumentTemplate
{
    /// <summary>
    /// The template of <paramref name="binding"/> for a file named <paramref name="fileName"/>; a
    /// <c>{newid:&lt;rule&gt;}</c> other than <c>document</c>, or one the specification gives no rule
    /// for, is refused, since no template of these types asks for one.
    /// </summary>
    public static string Text(FblBinding binding, DislSpecification specification, string fileName)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(fileName);
        var template = binding.Template ?? throw new ArgumentException($"The binding '{binding.Name}' has no template.", nameof(binding));
        return TemplateWriter.Replace(template.Text, fileName, rule => NewId(rule, specification));
    }

    private static string NewId(string rule, DislSpecification specification) =>
        rule == "document" && DislIds.NewDocumentId(specification) is { } id
            ? id
            : throw new InvalidOperationException($"The template asks for a new id of '{rule}', which this diagram type does not make.");
}
