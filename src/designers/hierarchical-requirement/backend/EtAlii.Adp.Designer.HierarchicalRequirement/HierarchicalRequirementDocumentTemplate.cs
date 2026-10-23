using System.Text;
using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// What a new requirement document starts as (FR-005): the binding's template, with the file's base name as its title,
/// written by <see cref="TemplateWriter.Produce"/> and so never written out a second time here. The binding creates its
/// registration with the first setting the person changes, so a new document is its Markdown file alone.
/// </summary>
internal sealed class HierarchicalRequirementDocumentTemplate : IDesignerDocumentTemplate
{
    public string Origin => Designer.Origin;

    public string? Create(DesignerFormat format, string fileName)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(fileName);
        if (!string.Equals(format.Extension, ".md", StringComparison.OrdinalIgnoreCase)) return null;

        // The template's numbers are written in it; it asks for no id of its own.
        var bytes = TemplateWriter.Produce(HierarchicalRequirementDefinition.Binding, Designer.Origin, fileName, rule => throw new InvalidOperationException($"The template asks for an id of '{rule}', which a requirement document gives by its counters."));
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }
}
