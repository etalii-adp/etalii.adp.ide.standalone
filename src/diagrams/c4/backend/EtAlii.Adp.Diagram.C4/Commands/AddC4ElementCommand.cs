using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

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

/// <summary>Undoes an <see cref="AddC4ElementCommand"/>: removes the element it declared.</summary>
/// <param name="CollapseParent">
/// The element whose block the add had to open, when it did - so undoing the add takes the
/// braces away again rather than leaving an empty block behind. Empty when the parent already
/// had a block, or when there was no parent at all.
/// </param>
public sealed record RemoveC4ElementCommand(string BodyPath, string Identifier, string CollapseParent = "") : IDocumentEditCommand;

internal sealed class AddC4ElementCommandHandler(IC4DocumentStore documents) : C4EditHandler<AddC4ElementCommand>(documents)
{
    /// <inheritdoc />
    protected override CommandResult Execute(AddC4ElementCommand command) =>
        command.Name.Trim().Length == 0
            ? CommandResult.Failure("An element needs a name.")
            : base.Execute(command);

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, C4Workspace model, AddC4ElementCommand command)
    {
        var name = command.Name.Trim();
        if (C4Placement.Refuse(model, command.Kind, command.ParentId) is { } refusal)
        {
            return EditOutcome.Refused(refusal);
        }

        var identifier = command.Identifier.Length > 0
            ? command.Identifier
            : C4Placement.IdentifierFor(model, name);
        if (model.Find(identifier) is not null)
        {
            return EditOutcome.Refused($"This model already has something called '{identifier}'.");
        }

        return C4Placement.TryInsert(body, model, command.Kind, name, identifier, command.ParentId, out var error, out var openedBlock)
            ? EditOutcome.UndoneBy(new RemoveC4ElementCommand(command.BodyPath, identifier, openedBlock ? command.ParentId : ""))
            : EditOutcome.Refused(error);
    }
}

internal sealed class RemoveC4ElementCommandHandler(IC4DocumentStore documents) : C4EditHandler<RemoveC4ElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, C4Workspace model, RemoveC4ElementCommand command)
    {
        if (model.Find(command.Identifier) is not { } element)
        {
            return EditOutcome.Refused("That element is no longer in this model.");
        }

        var inverse = new AddC4ElementCommand(
            command.BodyPath,
            element.Kind,
            element.Name,
            element.ParentId ?? "",
            element.Id);

        // Resolved before the removal shifts anything: the parent whose block the add had to
        // open, if it opened one.
        var parent = command.CollapseParent.Length > 0 ? model.Find(command.CollapseParent) : null;

        // Only ever undoing an add, so the element is a single line with no block of its own -
        // anything with children was not added this way and is not removed this way either.
        if (!C4Placement.TryRemove(body, element, out var error))
        {
            return EditOutcome.Refused(error);
        }

        if (parent is not null)
        {
            // The braces the add opened come off again. Without this, undoing an add would
            // leave `container "Web" … { }` behind - not what was there before, which is the
            // one thing undo has to deliver.
            C4Placement.CollapseEmptyBlock(body, parent);
        }

        return EditOutcome.UndoneBy(inverse);
    }
}
