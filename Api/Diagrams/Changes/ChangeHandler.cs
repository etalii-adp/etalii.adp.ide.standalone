namespace EtAlii.Adp.Api;

public abstract class ChangeHandler<TChange> : IChangeHandler
    where TChange : Change
{
    public Type ChangeType { get; } = typeof(TChange);

    Task IChangeHandler.Apply(Change change, AdpDbContext context) => Apply((TChange)change, context);
    Task IChangeHandler.Undo(Change change, AdpDbContext context) => Undo((TChange)change, context);
    Task IChangeHandler.Redo(Change change, AdpDbContext context) => Apply((TChange)change, context);

    protected abstract Task Apply(TChange change, AdpDbContext context);
    protected abstract Task Undo(TChange change, AdpDbContext context);
}