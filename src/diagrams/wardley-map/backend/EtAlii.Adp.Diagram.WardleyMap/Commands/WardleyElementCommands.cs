using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>Adds a `component`, `anchor` or `submap` statement (Requirement 9.3).</summary>
/// <param name="Kind">The keyword to write: `component`, `anchor` or `submap`.</param>
public sealed record AddWardleyElementCommand(
    string BodyPath,
    string Kind,
    string Name,
    double Visibility,
    double Maturity) : ICommand;

/// <summary>
/// Removes an element and every statement that referred to it (Requirement 9.3).
/// </summary>
/// <remarks>
/// Removing the declaration alone would leave links pointing at a name nothing declares - a
/// document ADP made invalid, which is worse than the edit the user asked for.
/// </remarks>
public sealed record RemoveWardleyElementCommand(string BodyPath, string ElementId) : ICommand;

/// <summary>Renames an element, rewriting every statement that names it (Requirement 4.4).</summary>
public sealed record RenameWardleyElementCommand(string BodyPath, string ElementId, string NewName) : ICommand;

/// <summary>Sets or clears `inertia` on a component (Requirement 6.2).</summary>
public sealed record SetWardleyInertiaCommand(string BodyPath, string ElementId, bool Inertia) : ICommand;

/// <summary>Sets or clears one of the five decorators on a component (Requirement 6.3).</summary>
public sealed record SetWardleyDecoratorCommand(
    string BodyPath,
    string ElementId,
    WardleyDecorator Decorator,
    bool Present) : ICommand;

/// <summary>
/// Restores the whole document. The inverse of any edit that touches more than one line - an
/// add, a remove, a rename.
/// </summary>
/// <remarks>
/// A per-line inverse cannot express these: a remove takes out several statements at once and a
/// rename rewrites a set of them, so putting the document back is the only description that is
/// both exact and simple. It is the same reasoning as
/// <see cref="RestoreWardleyLineCommand"/>, one scale up - and exactness is what makes an undo
/// leave no trace at all (Requirements 3.1, 9.6).
/// </remarks>
public sealed record RestoreWardleyDocumentCommand(string BodyPath, string Text) : ICommand;
