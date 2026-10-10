namespace EtAlii.Adp.Designer;

/// <summary>
/// The registered document templates by designer type: what the Add action asks for the content
/// of a new document, without knowing any designer type.
/// </summary>
public sealed class DesignerDocumentTemplates
{
    private readonly IReadOnlyDictionary<string, IDesignerDocumentTemplate> _byOrigin;

    public DesignerDocumentTemplates(IEnumerable<IDesignerDocumentTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        _byOrigin = templates.ToDictionary(template => template.Origin, StringComparer.Ordinal);
    }

    /// <summary>The template of the designer type <paramref name="origin"/>, or null when its module registered none.</summary>
    public IDesignerDocumentTemplate? Find(string origin) => _byOrigin.GetValueOrDefault(origin);
}
