using EtAlii.Adp.Designer;

namespace EtAlii.Adp.Sessions;

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
    /// <param name="root">The project's root folder.</param>
    /// <param name="registrationPath">
    /// The document's <c>.adp</c> registration, or null for a body recognised by its envelope that has none yet: the
    /// session then opens with the definition's defaults and writes nothing until the user changes a setting (spec 029 R4).
    /// </param>
    /// <param name="bodyPath">The file that holds the document.</param>
    IDesignerSession Open(ShortGuid watchId, ProjectRoot root, string? registrationPath, string bodyPath);
}
