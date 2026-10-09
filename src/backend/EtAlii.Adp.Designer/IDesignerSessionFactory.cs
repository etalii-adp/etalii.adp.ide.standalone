namespace EtAlii.Adp.Designer;

/// <summary>
/// How a designer module opens its sessions, keyed by the designer type's origin. The host
/// resolves which document a path names and which factory serves it; the module learns nothing
/// of the project beyond the root and the two files it is handed.
/// </summary>
public interface IDesignerSessionFactory
{
    /// <summary>The <see cref="DesignerDefinition.Origin"/> this factory serves.</summary>
    string Origin { get; }

    /// <param name="watchId">The connection the session belongs to.</param>
    /// <param name="rootPath">The project's root folder.</param>
    /// <param name="registrationPath">The document's <c>.adp</c> registration.</param>
    /// <param name="bodyPath">The file that holds the document.</param>
    IDesignerSession Open(ShortGuid watchId, string rootPath, string registrationPath, string bodyPath);
}
