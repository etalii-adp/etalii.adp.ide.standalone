using EtAlii.Adp.Files;
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
        ConnectAbmChildCommand connect => new ConnectAbmChildCommandHandler(documents).ExecuteAsync(connect, cancellationToken),
        ArrangeAbmNodeCommand arrange => new ArrangeAbmNodeCommandHandler(documents, new FileWriter()).ExecuteAsync(arrange, cancellationToken),
        RestoreAbmArrangementCommand restore => new RestoreAbmArrangementCommandHandler(documents, new FileWriter()).ExecuteAsync(restore, cancellationToken),
        SetRegistrationLayoutCommand layout => new SetRegistrationLayoutCommandHandler(new FileWriter()).ExecuteAsync(layout, cancellationToken),
        RemoveRegistrationLayoutCommand layout => new RemoveRegistrationLayoutCommandHandler(new FileWriter()).ExecuteAsync(layout, cancellationToken),
        ArrangeAbmCommand arrange => new ArrangeAbmCommandHandler(new FileWriter()).ExecuteAsync(arrange, cancellationToken),
        RestoreAbmRegistrationCommand restore => new RestoreAbmRegistrationCommandHandler(new FileWriter()).ExecuteAsync(restore, cancellationToken),
        RestoreDocumentCommand<IAbmDocumentStore> restore => new RestoreDocumentCommandHandler<IAbmDocumentStore>(documents, new FileWriter()).ExecuteAsync(restore, cancellationToken),
        _ => throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}."),
    };
}
