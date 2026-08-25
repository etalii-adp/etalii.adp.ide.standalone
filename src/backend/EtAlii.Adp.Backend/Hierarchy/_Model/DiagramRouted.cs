using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// The file is a diagram of <paramref name="Definition"/>'s type. <paramref name="BodyPath"/>
/// is where its document lives - the registration file itself for a type with no sibling,
/// whether or not that file exists yet. <paramref name="RegistrationPath"/> is null for a
/// body opened without one (Requirement 2.7), which is never created implicitly.
/// </summary>
/// <param name="BodyPath">
/// Null when the registration names a body through a <c>body:</c> header and the router was not
/// told which project root to resolve it against. It is nullable rather than empty on purpose:
/// an empty string looks like a path right up until something calls
/// <see cref="System.IO.Path.GetFullPath(string)"/> on it, which is how a whole project's
/// validation once died with "The path is empty". A caller that needs the body passes the root;
/// one that only wants the type reads <paramref name="Definition"/> and never asks.
/// </param>
public sealed record DiagramRouted(DiagramDefinition Definition, string? RegistrationPath, string? BodyPath) : DiagramRouting;
