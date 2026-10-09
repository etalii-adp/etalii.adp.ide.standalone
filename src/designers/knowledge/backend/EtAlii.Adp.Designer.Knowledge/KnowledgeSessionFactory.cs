using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>Opens a session per connection for a knowledge file. Each connection has its own view and its own window.</summary>
internal sealed class KnowledgeSessionFactory : IDesignerSessionFactory
{
    public string Origin => Designer.Origin;

    public IDesignerSession Open(ShortGuid watchId, string rootPath, string registrationPath, string bodyPath) => new KnowledgeSession(bodyPath);
}

/// <summary>
/// What a new knowledge file starts as: the template of the binding its format selects, with a
/// fresh id for everything the template makes - one title property, one view and no rows.
/// </summary>
internal sealed class KnowledgeDocumentTemplate : IDesignerDocumentTemplate
{
    public string Origin => Designer.Origin;

    public string? Create(DesignerFormat format, string fileName)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(fileName);

        if (KnowledgeDefinition.BindingFor(format.Extension) is not { } binding)
        {
            return null;
        }

        // An id is a ShortGuid: never reused, and never derived from anything that could change.
        var bytes = TemplateWriter.Produce(binding, Designer.Origin, fileName, _ => ShortGuid.NewShortGuid().ToString());
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }
}
