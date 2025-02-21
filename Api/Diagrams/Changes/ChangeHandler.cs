namespace EtAlii.Adp.Api;

public abstract class ChangeHandler<TChange> : IChangeHandler
    where TChange : Change
{
    public Type ChangeType { get; } = typeof(TChange);

    Task IChangeHandler.Apply(Change change, AdpDbContext context)
    {
        return change.Undo 
            ? Undo((TChange)change, context) 
            : Do((TChange)change, context);
    }
    
    protected abstract Task Do(TChange change, AdpDbContext context);
    protected abstract Task Undo(TChange change, AdpDbContext context);
}