using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class TogglePublicAccessCommandHandler : CommandHandler<TogglePublicAccessCommand>
{
    protected override async Task Do(TogglePublicAccessCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(n => n.Id == command.Id);
        
        diagram.AllowPublicAccess = command.NewAllowPublicAccess;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }

    protected override async Task Undo(TogglePublicAccessCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(n => n.Id == command.Id);
        
        diagram.AllowPublicAccess = command.OldAllowPublicAccess;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
}