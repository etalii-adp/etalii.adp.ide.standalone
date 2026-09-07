

using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>Adds a `component`, `anchor` or `submap` statement (Requirement 9.3).</summary>
/// <param name="Kind">The keyword to write: `component`, `anchor` or `submap`.</param>
/// <param name="Decorator">
/// A decorator the new element carries from the moment it is written. This is what makes the
/// toolbox's <b>Market</b> and <b>Ecosystem</b> entries possible: Requirement 13.2 says they
/// drop a component <em>carrying that decorator</em> rather than an element kind of their own,
/// and doing it in this one command is what keeps one drop to one undo.
/// </param>
public sealed record AddWardleyElementCommand(
    string BodyPath,
    string Kind,
    string Name,
    double Visibility,
    double Maturity,
    WardleyDecorator? Decorator = null) : ICommand;

/// <summary>Adds a `note` - free text pinned at a position (Requirement 6.7).</summary>
public sealed record AddWardleyNoteCommand(
    string BodyPath,
    string Text,
    double Visibility,
    double Maturity) : ICommand;

/// <summary>
/// Adds a numbered `annotation` (Requirement 6.8).
/// </summary>
/// <remarks>
/// The number is chosen by the command rather than passed in: the map's annotations are a
/// numbered sequence the map itself shows, so a caller picking the number could produce two
/// annotation 3s - which the format allows to be written and no reader can make sense of.
/// </remarks>
public sealed record AddWardleyAnnotationCommand(
    string BodyPath,
    string Text,
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

/// <summary>Sets or clears a link between two elements (Requirement 9.3).</summary>
/// <param name="Kind">
/// Dependency or flow. Part of what identifies a link rather than a property of it: the DSL
/// writes the two with different arrows, and a map may carry one of each between one pair.
/// </param>
/// <param name="Context">
/// The text after the `;`, which is what a flow link is usually for. Empty writes the link with
/// no context at all rather than with an empty one.
/// </param>
public sealed record SetWardleyLinkCommand(
    string BodyPath,
    string SourceElementId,
    string TargetElementId,
    WardleyLinkKind Kind,
    bool Present,
    string Context = "") : ICommand;

/// <summary>
/// Sets or clears an element's `evolve` statement - where its owner believes it is heading
/// (Requirement 6.1).
/// </summary>
/// <param name="OverrideName">
/// What the component is called once it arrives, from the `evolve Name-&gt;NewName x` form.
/// Empty keeps the name it has.
/// </param>
public sealed record SetWardleyEvolveCommand(
    string BodyPath,
    string ElementId,
    bool Present,
    double Maturity,
    string OverrideName = "") : ICommand;

/// <summary>Adds a component to a pipeline, or takes one out (Requirement 9.3).</summary>
/// <param name="ParentElementId">The component whose pipeline this is; a component has at most one.</param>
/// <param name="ChildName">The child's name, which is its handle inside the block.</param>
/// <param name="Maturity">
/// Where on the evolution axis the child sits. One number, because a child takes its visibility
/// from its parent (Requirement 5.4).
/// </param>
public sealed record SetWardleyPipelineMembershipCommand(
    string BodyPath,
    string ParentElementId,
    string ChildName,
    bool Member,
    double Maturity = 0.5d) : ICommand;

/// <summary>
/// Sets a component's decorators to exactly this set (Requirement 6.3).
/// </summary>
/// <remarks>
/// The whole set in one command, because the property grid shows the decorators as one row -
/// "what is set", per Requirement 15.2 - and editing one row must be one undo. Doing it as
/// several <see cref="SetWardleyDecoratorCommand"/>s would make a single typed change take
/// several presses of Ctrl+Z to put back.
/// </remarks>
public sealed record SetWardleyDecoratorsCommand(
    string BodyPath,
    string ElementId,
    IReadOnlyList<WardleyDecorator> Decorators) : ICommand;

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
