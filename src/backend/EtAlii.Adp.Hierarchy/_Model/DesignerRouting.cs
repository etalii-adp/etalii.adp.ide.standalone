using EtAlii.Adp.Designer;

namespace EtAlii.Adp.Hierarchy;

/// <summary>What <see cref="DesignerFileRouter"/> makes of a file on disk.</summary>
public abstract record DesignerRouting;

/// <summary>
/// The file belongs to a document of <paramref name="Definition"/>'s designer type: it is the
/// registration at <paramref name="RegistrationPath"/>, or the body that registration names.
/// </summary>
/// <param name="Definition">The designer type the registration's first line names.</param>
/// <param name="RegistrationPath">The <c>.adp</c> file. A designer's document always has one.</param>
/// <param name="BodyPath">
/// Where the document's content lives: the file a <c>body:</c> header names, or the sibling of
/// the registration's own name in one of the type's formats. Null when there is no such file,
/// or when the header could not be followed because the router was given no project root -
/// nullable rather than empty for the reason <see cref="DiagramRouted"/> gives.
/// </param>
public sealed record DesignerRouted(DesignerDefinition Definition, string RegistrationPath, string? BodyPath) : DesignerRouting;

/// <summary>
/// The registration names a designer type, and cannot be followed: its <c>body:</c> header
/// names a file outside the project, which is refused rather than read.
/// </summary>
public sealed record DesignerUnreadable(string Path, string Reason) : DesignerRouting;

/// <summary>No designer type claims the file.</summary>
public sealed record NotADesigner : DesignerRouting;
