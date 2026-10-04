using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>Routes each command to its handler, as the host's dispatcher does, so a real history can run them.</summary>
internal sealed class AbmTestDispatcher(IAbmDocumentStore documents) : ICommandDispatcher
{
    public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) => command switch
    {
        AddAbmNodeCommand add => new AddAbmNodeCommandHandler(documents).ExecuteAsync(add, cancellationToken),
        RemoveAbmNodeCommand remove => new RemoveAbmNodeCommandHandler(documents).ExecuteAsync(remove, cancellationToken),
        RenameAbmNodeCommand rename => new RenameAbmNodeCommandHandler(documents).ExecuteAsync(rename, cancellationToken),
        SetAbmNodeKindCommand kind => new SetAbmNodeKindCommandHandler(documents).ExecuteAsync(kind, cancellationToken),
        SetAbmNotesCommand notes => new SetAbmNotesCommandHandler(documents).ExecuteAsync(notes, cancellationToken),
        MoveAbmNodeCommand move => new MoveAbmNodeCommandHandler(documents).ExecuteAsync(move, cancellationToken),
        SetRegistrationLayoutCommand layout => new SetRegistrationLayoutCommandHandler().ExecuteAsync(layout, cancellationToken),
        RemoveRegistrationLayoutCommand layout => new RemoveRegistrationLayoutCommandHandler().ExecuteAsync(layout, cancellationToken),
        ArrangeAbmCommand arrange => new ArrangeAbmCommandHandler().ExecuteAsync(arrange, cancellationToken),
        RestoreAbmRegistrationCommand restore => new RestoreAbmRegistrationCommandHandler().ExecuteAsync(restore, cancellationToken),
        RestoreDocumentCommand<IAbmDocumentStore> restore => new RestoreDocumentCommandHandler<IAbmDocumentStore>(documents).ExecuteAsync(restore, cancellationToken),
        _ => throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}."),
    };
}
