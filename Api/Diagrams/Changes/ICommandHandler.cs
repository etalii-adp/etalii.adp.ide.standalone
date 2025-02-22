namespace EtAlii.Adp.Api;

public interface ICommandHandler
{
    Type CommandType { get; }
    Task Apply(Command command, AdpDbContext context);
}