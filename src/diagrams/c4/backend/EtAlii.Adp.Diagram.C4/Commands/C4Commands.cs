using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>Rename an element (Requirement 13.1).</summary>
public sealed record SetElementNameCommand(string BodyPath, string ElementId, string Name) : IDocumentEditCommand;

/// <summary>Set an element's description - C4 asks for one on every element (Requirement 10.2).</summary>
public sealed record SetElementDescriptionCommand(string BodyPath, string ElementId, string Description) : IDocumentEditCommand;

/// <summary>Set a container's or component's technology - C4 requires one explicitly (Requirement 10.3).</summary>
public sealed record SetElementTechnologyCommand(string BodyPath, string ElementId, string Technology) : IDocumentEditCommand;

/// <summary>Relabel a relationship - C4 requires the label to say what it is for (Requirement 10.4).</summary>
public sealed record SetRelationshipDescriptionCommand(string BodyPath, string RelationshipId, string Description) : IDocumentEditCommand;

/// <summary>Set the protocol two containers communicate over (Requirement 10.5).</summary>
public sealed record SetRelationshipTechnologyCommand(string BodyPath, string RelationshipId, string Technology) : IDocumentEditCommand;

/// <summary>
/// What every C4 command handler needs: the reading, and the element or relationship the command names. Every
/// value is written by the library as a splice (c4-disl-fbl task 15): a changed value changes only its bytes, an
/// absent argument is written in its own position, before any brace and any comment, and everything else in the body
/// keeps the bytes it arrived as. Resolution failures are refusals rather than exceptions, because a stale canvas
/// asking about something that has since been deleted is ordinary. The run itself - load, edit a copy, save - is <see cref="DocumentEditRun"/>'s.
/// </summary>
public abstract class C4EditHandler<TCommand>(IC4DocumentStore documents)
    : DocumentEditHandler<TCommand, IC4DocumentStore, LineDocument, C4Workspace>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <inheritdoc />
    protected sealed override C4Workspace Parse(LineDocument body) => C4Body.Read(body.Text).Workspace;

    /// <summary>Sets <paramref name="attribute"/> of the element <paramref name="elementId"/> names to <paramref name="value"/>.</summary>
    protected static EditOutcome EditElement(LineDocument document, TCommand command, string elementId, string attribute, string value) =>
        C4Body.Write(
            document,
            command,
            body => body.ReadingIdOf(elementId) is { } id ? new ModelChange.Set(id, new Dictionary<string, object?> { [attribute] = value }) : null,
            $"'{elementId}' is not in this model any more.");

    /// <summary>Sets <paramref name="attribute"/> of the stored relationship <paramref name="relationshipId"/> names to <paramref name="value"/>.</summary>
    protected static EditOutcome EditRelationship(LineDocument document, TCommand command, string relationshipId, string attribute, string value) =>
        C4Body.Write(
            document,
            command,
            body => body.RelationshipIdOf(relationshipId) is { } id ? new ModelChange.Set(id, new Dictionary<string, object?> { [attribute] = value }) : null,
            "That relationship is not in this model any more.");
}

internal sealed class SetElementNameCommandHandler(IC4DocumentStore documents) : C4EditHandler<SetElementNameCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, C4Workspace model, SetElementNameCommand command)
    {
        var element = model.Find(command.ElementId);
        if (element is not null && element.Kind is C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance)
        {
            // An instance takes (what it is an instance of, deployment groups, tags) and has no name of its own.
            return EditOutcome.Refused(
                $"A {element.Kind.ToString().ToLowerInvariant()} has no name in C4; it stands for what it is an instance of.");
        }

        return EditElement(body, command, command.ElementId, "name", command.Name);
    }
}

internal sealed class SetElementDescriptionCommandHandler(IC4DocumentStore documents) : C4EditHandler<SetElementDescriptionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, C4Workspace model, SetElementDescriptionCommand command)
    {
        var element = model.Find(command.ElementId);
        if (element is not null && element.Kind is C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance)
        {
            // As for the name: an instance takes what it is an instance of, deployment groups and tags, and the binding
            // holds no description for it.
            return EditOutcome.Refused(
                $"A {element.Kind.ToString().ToLowerInvariant()} has no description in C4; it stands for what it is an instance of.");
        }

        return EditElement(body, command, command.ElementId, "description", command.Description);
    }
}

internal sealed class SetElementTechnologyCommandHandler(IC4DocumentStore documents) : C4EditHandler<SetElementTechnologyCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, C4Workspace model, SetElementTechnologyCommand command)
    {
        var element = model.Find(command.ElementId);
        if (element is not null && element.Kind is not (C4ElementKind.Container or C4ElementKind.Component
            or C4ElementKind.DeploymentNode or C4ElementKind.InfrastructureNode))
        {
            // A person and a software system take (name, description, tags) and have no technology.
            return EditOutcome.Refused(
                $"A {element.Kind.ToString().ToLowerInvariant()} has no technology in C4; only containers, components and infrastructure do.");
        }

        return EditElement(body, command, command.ElementId, "technology", command.Technology);
    }
}

internal sealed class SetRelationshipDescriptionCommandHandler(IC4DocumentStore documents) : C4EditHandler<SetRelationshipDescriptionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, C4Workspace model, SetRelationshipDescriptionCommand command) =>
        EditRelationship(body, command, command.RelationshipId, "description", command.Description);
}

internal sealed class SetRelationshipTechnologyCommandHandler(IC4DocumentStore documents) : C4EditHandler<SetRelationshipTechnologyCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, C4Workspace model, SetRelationshipTechnologyCommand command) =>
        EditRelationship(body, command, command.RelationshipId, "technology", command.Technology);
}
