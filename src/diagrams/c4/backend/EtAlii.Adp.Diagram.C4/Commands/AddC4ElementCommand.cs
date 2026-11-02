using System.Globalization;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>Adds one element to a C4 model, inside <paramref name="ParentId"/> or at the top when it is empty.</summary>
/// <param name="ParentId">
/// The element the new one goes inside - a software system for a container, a container for a
/// component. Empty puts it at the top of the model, which is where a person and a software
/// system belong.
/// </param>
/// <param name="Identifier">
/// The DSL identifier to declare it under. Chosen by the handler when empty, which is the
/// normal case: an identifier is ADP's business rather than the user's.
/// </param>
public sealed record AddC4ElementCommand(
    string BodyPath,
    C4ElementKind Kind,
    string Name,
    string ParentId = "",
    string Identifier = "") : IDocumentEditCommand;

/// <summary>Removes one element from a C4 model, as the binding removes it.</summary>
/// <param name="CollapseParent">
/// No longer read: an add is undone by putting back the text it started from
/// (<see cref="RestoreDocumentCommand{TStore}"/>), which takes away a block the add opened, so no
/// removal has to close one; kept so the record keeps its shape.
/// </param>
public sealed record RemoveC4ElementCommand(
    string BodyPath,
    string Identifier,
    // ReSharper disable once NotAccessedPositionalProperty.Global - Reason: an undo now restores the text before the edit, so nothing reads CollapseParent; it is kept because c4-disl-fbl task 15 keeps every command record as it was.
    string CollapseParent = "") : IDocumentEditCommand;

/// <summary>
/// Adds an element as the definition's add operation creates it - <c>&lt;identifier&gt; = &lt;keyword&gt; "Name"
/// "Describe Name."</c>, with <c>"Technology"</c> for a container or a component - written by the library where the
/// binding places it (c4-disl-fbl task 15, Requirement 5.4): a person or a software system at the end of the model, a
/// container or a component as the last child of its parent, which gains a block when it has none. Undoing it puts
/// back the text it started from.
/// </summary>
internal sealed class AddC4ElementCommandHandler(IC4DocumentStore documents) : C4EditHandler<AddC4ElementCommand>(documents)
{
    /// <summary>What the library says of an add into a block that is not closed.</summary>
    private const string UnclosedBlock = "The block is not closed, so nothing can be added to it.";

    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(AddC4ElementCommand command, CancellationToken cancellationToken) =>
        command.Name.Trim().Length == 0
            ? CommandResult.Failure("An element needs a name.")
            : await base.RunAsync(command, cancellationToken);

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, C4Workspace model, AddC4ElementCommand command)
    {
        var name = command.Name.Trim();
        if (Refuse(model, command.Kind, command.ParentId) is { } refusal)
        {
            return EditOutcome.Refused(refusal);
        }

        var identifier = command.Identifier.Length > 0
            ? command.Identifier
            : IdentifierFor(model, name);
        if (model.Find(identifier) is not null)
        {
            return EditOutcome.Refused($"This model already has something called '{identifier}'.");
        }

        // The text is the definition's: its add operation, run with the identifier made here (gap C11), gives the
        // attributes and whether the element goes inside the parent at all, which a person or a software system
        // dropped on another element does not.
        var operation = C4ContextActionProvider.AddableKinds.First(addable => addable.Kind == command.Kind).Operation;
        if (C4Definitions.Creation(model, C4Body.FileOf(command.BodyPath), null, null, operation, command.ParentId, name, identifier) is not { } created)
        {
            return EditOutcome.Refused("The element it would go inside is no longer in this model.");
        }

        var outcome = C4Body.Write(
            body,
            command,
            read => created.ParentId is null
                ? new ModelChange.Add(command.Kind.ToString(), identifier, created.Attributes)
                : read.ReadingIdOf(command.ParentId) is { } parent
                    ? new ModelChange.Add(command.Kind.ToString(), identifier, created.Attributes, parent)
                    : null,
            "The element it would go inside is no longer in this model.");

        // A block that is never closed is the library's refusal too; it is said as it was said before (Requirement 5.5).
        return outcome.Refusal == UnclosedBlock
            ? EditOutcome.Refused(created.ParentId is null
                ? "This document has no model block to add an element to."
                : "That element's block is not closed, so nothing can be added to it.")
            : outcome;
    }

    /// <summary>Why this kind cannot go there, or null when it can.</summary>
    private static string? Refuse(C4Workspace workspace, C4ElementKind kind, string parentId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(parentId);
        var parent = parentId.Length == 0 ? null : workspace.Find(parentId);
        if (parentId.Length > 0 && parent is null)
        {
            return "The element it would go inside is no longer in this model.";
        }

        return kind switch
        {
            // The two things that stand on their own. Dropping one onto something else is a
            // gesture that means nothing rather than one that is forbidden, so it is taken as
            // "at the top" rather than refused.
            C4ElementKind.Person or C4ElementKind.SoftwareSystem => null,

            C4ElementKind.Container when parent is null =>
                "A container lives inside a software system. Drop it on one.",
            C4ElementKind.Container when parent.Kind != C4ElementKind.SoftwareSystem =>
                $"A container lives inside a software system, not inside a {Spell(parent.Kind)}.",

            C4ElementKind.Component when parent is null =>
                "A component lives inside a container. Drop it on one.",
            C4ElementKind.Component when parent.Kind != C4ElementKind.Container =>
                $"A component lives inside a container, not inside a {Spell(parent.Kind)}.",

            C4ElementKind.Container or C4ElementKind.Component => null,

            // Deployment nodes, infrastructure nodes and instances live in a deployment
            // environment, which is a different gesture.
            _ => $"Adding a {Spell(kind)} from the toolbox is not supported yet.",
        };
    }

    /// <summary>An identifier nothing in the model is using yet, derived from the name (gap C11, finding A9).</summary>
    internal static string IdentifierFor(C4Workspace workspace, string name)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder();
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(builder.Length == 0 ? char.ToLowerInvariant(character) : character);
            }
        }

        var candidate = builder.Length == 0 ? "element" : builder.ToString();
        if (char.IsDigit(candidate[0]))
        {
            candidate = "_" + candidate;
        }

        if (workspace.Find(candidate) is null)
        {
            return candidate;
        }

        // `web`, `web2`, `web3`. Numbered rather than randomised, because an identifier is what
        // a human reads in the diff.
        for (var suffix = 2; ; suffix++)
        {
            var numbered = candidate + suffix.ToString(CultureInfo.InvariantCulture);
            if (workspace.Find(numbered) is null)
            {
                return numbered;
            }
        }
    }

    private static string Spell(C4ElementKind kind) => kind switch
    {
        C4ElementKind.SoftwareSystem => "software system",
        C4ElementKind.DeploymentNode => "deployment node",
        C4ElementKind.InfrastructureNode => "infrastructure node",
        C4ElementKind.ContainerInstance => "container instance",
        C4ElementKind.SoftwareSystemInstance => "software system instance",
        _ => kind.ToString().ToLowerInvariant(),
    };
}

/// <summary>
/// Removes an element as the binding removes it: its entry with the comments that lead it, and what the binding's
/// <c>remove.cascade</c> takes with it, written by the library. An element with things inside it is refused rather
/// than taking them too.
/// </summary>
internal sealed class RemoveC4ElementCommandHandler(IC4DocumentStore documents) : C4EditHandler<RemoveC4ElementCommand>(documents)
{
    private const string Gone = "That element is no longer in this model.";

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, C4Workspace model, RemoveC4ElementCommand command)
    {
        if (model.Find(command.Identifier) is not { } element)
        {
            return EditOutcome.Refused(Gone);
        }

        if (model.Elements.Any(child => child.ParentId is { } parent && string.Equals(parent, element.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return EditOutcome.Refused("That element has things inside it, so removing it here would take them too.");
        }

        return C4Body.Write(body, command, read => read.ReadingIdOf(element.Id) is { } id ? new ModelChange.Remove(id) : null, Gone);
    }
}
