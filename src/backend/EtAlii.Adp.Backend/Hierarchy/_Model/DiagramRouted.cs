using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// The file is a diagram of <paramref name="Definition"/>'s type. <paramref name="BodyPath"/>
/// is where its document lives - the registration file itself for a type with no sibling,
/// whether or not that file exists yet. <paramref name="RegistrationPath"/> is null for a
/// body opened without one (Requirement 2.7), which is never created implicitly.
/// </summary>
public sealed record DiagramRouted(DiagramDefinition Definition, string? RegistrationPath, string BodyPath) : DiagramRouting;
