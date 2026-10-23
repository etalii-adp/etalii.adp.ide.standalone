using EtAlii.Adp.Designer;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>What a new requirements document starts as: the envelope and a heading of its name.</summary>
internal sealed class RequirementsTemplate : IDesignerDocumentTemplate
{
    public string Origin => RequirementsDesigner.Origin;

    public string Create(DesignerFormat format, string fileName) => RequirementsDesigner.Body(Path.GetFileNameWithoutExtension(fileName));
}
