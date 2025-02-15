namespace EtAlii.Adp.Api;

public interface IChangeHandler
{
    Type ChangeType { get; }
    Task Apply(Change change, AdpDbContext context);
    Task Undo(Change change, AdpDbContext context);
    Task Redo(Change change, AdpDbContext context);
}