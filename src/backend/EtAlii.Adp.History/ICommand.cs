namespace EtAlii.Adp.History;

/// <summary>
/// An intent to change state. Commands are plain data: they carry <em>what</em> should
/// happen, never <em>how</em> - that belongs to the matching <see cref="ICommandHandler{TCommand}"/>.
/// </summary>
/// <remarks>
/// Because a command is data, the same instance can be replayed later by
/// <see cref="IHistoryStack"/> to redo it. Implementations should therefore be immutable
/// (a <c>record</c> is the natural choice) and must not capture live state such as an open
/// stream or a connection.
/// </remarks>
public interface ICommand;
