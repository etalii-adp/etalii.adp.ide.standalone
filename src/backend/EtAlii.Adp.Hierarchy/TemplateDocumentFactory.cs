using System.Text;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The new document of a type whose binding has a <c>template</c> (FBL 13), made by
/// <see cref="TemplateWriter.Produce"/>: <c>template.byOrigin</c> for the type's origin before
/// <c>template.text</c>, with the placeholders replaced for the file core will write. A module whose
/// binding states its new document registers one of these instead of writing that text a second
/// time (dotnet-dependency-graph-disl-fbl library item L6).
/// </summary>
/// <remarks>
/// <para>
/// Tool-neutral: everything it knows comes from the definition and the binding it is given. The
/// file name it fills <c>{name}</c> with is the base name plus the definition's primary extension,
/// which is the name <c>DiagramAddOptions</c> gives a new body.
/// </para>
/// <para>
/// It never writes. Core writes what it returns, and refuses to create at a name anything already
/// occupies, so a document made from a template is never written over an existing file.
/// </para>
/// <para>
/// In <c>EtAlii.Adp.Hierarchy</c>, beside the Add that calls it, because <c>EtAlii.Adp.Documents</c>,
/// which declares <see cref="IDiagramDocumentFactory"/>, does not reference the FBL library (the
/// placement ruling of the specification's design).
/// </para>
/// </remarks>
public sealed class TemplateDocumentFactory : IDiagramDocumentFactory
{
    private readonly DiagramDefinition _definition;
    private readonly FblBinding _binding;
    private readonly Func<string, string> _newId;

    /// <param name="definition">The type whose new document this makes: its origin and its primary extension.</param>
    /// <param name="binding">The type's binding, which must have a <c>template</c>.</param>
    /// <param name="newId">
    /// Makes an id for <c>{newid:&lt;rule&gt;}</c> from the rule's name; a new short GUID when not given.
    /// </param>
    public TemplateDocumentFactory(DiagramDefinition definition, FblBinding binding, Func<string, string>? newId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(binding);
        if (!definition.HasDocumentSibling)
        {
            throw new ArgumentException($"{definition.Origin} declares no extension, so it has no document for a template to make.", nameof(definition));
        }

        if (binding.Template is null)
        {
            throw new ArgumentException($"The binding '{binding.Name}' has no template to make a new document of {definition.Origin} from.", nameof(binding));
        }

        _definition = definition;
        _binding = binding;
        _newId = newId ?? (_ => ShortGuid.NewShortGuid().ToString());
    }

    /// <inheritdoc />
    public ToolOrigin Origin => _definition.Origin;

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        var bytes = TemplateWriter.Produce(_binding, _definition.Origin.Key, baseName + _definition.Extension, _newId)!;
        return Encoding.UTF8.GetString(bytes);
    }
}
