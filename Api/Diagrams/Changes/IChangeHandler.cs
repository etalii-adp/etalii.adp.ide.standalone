namespace EtAlii.Adp.Api;

public interface IChangeHandler
{
    Type ChangeType { get; }
    Task Apply(Change change, AdpDbContext context);
}