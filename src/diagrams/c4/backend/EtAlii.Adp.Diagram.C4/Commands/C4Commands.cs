using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>Rename an element (Requirement 13.1).</summary>
public sealed record SetElementNameCommand(string BodyPath, string ElementId, string Name) : ICommand;

/// <summary>Set an element's description - C4 asks for one on every element (Requirement 10.2).</summary>
public sealed record SetElementDescriptionCommand(string BodyPath, string ElementId, string Description) : ICommand;

/// <summary>Set a container's or component's technology - C4 requires one explicitly (Requirement 10.3).</summary>
public sealed record SetElementTechnologyCommand(string BodyPath, string ElementId, string Technology) : ICommand;

/// <summary>Relabel a relationship - C4 requires the label to say what it is for (Requirement 10.4).</summary>
public sealed record SetRelationshipDescriptionCommand(string BodyPath, string RelationshipId, string Description) : ICommand;

/// <summary>Set the protocol two containers communicate over (Requirement 10.5).</summary>
public sealed record SetRelationshipTechnologyCommand(string BodyPath, string RelationshipId, string Technology) : ICommand;

/// <summary>
/// What every C4 command handler needs: the document, the element or relationship the command
/// names, and the line it sits on. Resolution failures are refusals rather than exceptions,
/// because a stale canvas asking about something that has since been deleted is ordinary.
/// </summary>
public abstract class C4CommandHandler(IC4DocumentStore documents)
{
    protected IC4DocumentStore Documents { get; } = documents;

    /// <summary>
    /// Rewrites one argument of the line an element was declared on. Everything else on that
    /// line, and every other line, keeps the bytes it arrived as (Requirement 3.2).
    /// </summary>
    protected CommandResult EditElement(
        string bodyPath,
        string elementId,
        Func<C4Element, int> argumentIndex,
        string value,
        Func<C4Element, string> previous,
        Func<string, ICommand> inverse)
    {
        var workspace = Documents.WorkspaceOf(bodyPath);
        var element = workspace.Find(elementId);
        if (element is null)
        {
            return CommandResult.Failure($"'{elementId}' is not in this model any more.");
        }

        var document = Documents.GetOrLoad(bodyPath);
        var line = document.Lines[(int)element.Line - 1];
        var keyword = KeywordIndexOf(line);
        if (keyword < 0)
        {
            return CommandResult.Failure($"Line {element.Line} does not look like an element declaration any more.");
        }

        var was = previous(element);
        document.ReplaceLine(element.Line, C4Tokens.ReplaceArgument(line, keyword, argumentIndex(element), value));
        var saved = Documents.Save(bodyPath, document);
        return !saved.Failed
            ? CommandResult.Success(inverse(was))
            : CommandResult.Failure(saved.Error);
    }

    /// <summary>The same, for a relationship, whose arguments follow the destination.</summary>
    protected CommandResult EditRelationship(
        string bodyPath,
        string relationshipId,
        int argumentIndex,
        string value,
        Func<C4Relationship, string> previous,
        Func<string, ICommand> inverse)
    {
        var workspace = Documents.WorkspaceOf(bodyPath);
        var relationship = workspace.Relationships.FirstOrDefault(candidate => candidate.Id == relationshipId);
        if (relationship is null)
        {
            return CommandResult.Failure("That relationship is not in this model any more.");
        }

        var document = Documents.GetOrLoad(bodyPath);
        var line = document.Lines[(int)relationship.Line - 1];
        var tokens = C4Tokens.SplitWithSpans(line);
        // `source -> destination "description" "technology"`, or `-> destination ...` inside an
        // element block. Either way the arguments follow the destination.
        var arrow = -1;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!tokens[index].Quoted && tokens[index].Value == "->")
            {
                arrow = index;
                break;
            }
        }

        if (arrow < 0 || arrow + 1 >= tokens.Count)
        {
            return CommandResult.Failure($"Line {relationship.Line} does not look like a relationship any more.");
        }

        var was = previous(relationship);
        document.ReplaceLine(relationship.Line, C4Tokens.ReplaceArgument(line, arrow + 1, argumentIndex, value));
        var saved = Documents.Save(bodyPath, document);
        return !saved.Failed
            ? CommandResult.Success(inverse(was))
            : CommandResult.Failure(saved.Error);
    }

    /// <summary>
    /// Where an element's arguments start on its line: after `identifier = keyword`, or after
    /// the keyword when the declaration named no identifier.
    /// </summary>
    private static int KeywordIndexOf(string line)
    {
        var tokens = C4Tokens.SplitWithSpans(line);
        if (tokens.Count >= 3 && tokens[1].Value == "=")
        {
            return 2;
        }

        return tokens.Count >= 1 ? 0 : -1;
    }
}

internal sealed class SetElementNameCommandHandler(IC4DocumentStore documents)
    : C4CommandHandler(documents), ICommandHandler<SetElementNameCommand>
{
    public Task<CommandResult> ExecuteAsync(SetElementNameCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(EditElement(
            command.BodyPath,
            command.ElementId,
            _ => 0,
            command.Name,
            element => element.Name,
            was => command with { Name = was }));
    }
}

internal sealed class SetElementDescriptionCommandHandler(IC4DocumentStore documents)
    : C4CommandHandler(documents), ICommandHandler<SetElementDescriptionCommand>
{
    public Task<CommandResult> ExecuteAsync(SetElementDescriptionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(EditElement(
            command.BodyPath,
            command.ElementId,
            _ => 1,
            command.Description,
            element => element.Description,
            was => command with { Description = was }));
    }
}

internal sealed class SetElementTechnologyCommandHandler(IC4DocumentStore documents)
    : C4CommandHandler(documents), ICommandHandler<SetElementTechnologyCommand>
{
    public Task<CommandResult> ExecuteAsync(SetElementTechnologyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = Documents.WorkspaceOf(command.BodyPath);
        var element = workspace.Find(command.ElementId);
        if (element is not null && element.Kind is not (C4ElementKind.Container or C4ElementKind.Component
            or C4ElementKind.DeploymentNode or C4ElementKind.InfrastructureNode))
        {
            // A person and a software system take (name, description, tags) - writing a
            // technology into position 2 would turn their tags into one.
            return Task.FromResult(CommandResult.Failure(
                $"A {element.Kind.ToString().ToLowerInvariant()} has no technology in C4; only containers, components and infrastructure do."));
        }

        return Task.FromResult(EditElement(
            command.BodyPath,
            command.ElementId,
            _ => 2,
            command.Technology,
            e => e.Technology,
            was => command with { Technology = was }));
    }
}

internal sealed class SetRelationshipDescriptionCommandHandler(IC4DocumentStore documents)
    : C4CommandHandler(documents), ICommandHandler<SetRelationshipDescriptionCommand>
{
    public Task<CommandResult> ExecuteAsync(SetRelationshipDescriptionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(EditRelationship(
            command.BodyPath,
            command.RelationshipId,
            0,
            command.Description,
            relationship => relationship.Description,
            was => command with { Description = was }));
    }
}

internal sealed class SetRelationshipTechnologyCommandHandler(IC4DocumentStore documents)
    : C4CommandHandler(documents), ICommandHandler<SetRelationshipTechnologyCommand>
{
    public Task<CommandResult> ExecuteAsync(SetRelationshipTechnologyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(EditRelationship(
            command.BodyPath,
            command.RelationshipId,
            1,
            command.Technology,
            relationship => relationship.Technology,
            was => command with { Technology = was }));
    }
}
