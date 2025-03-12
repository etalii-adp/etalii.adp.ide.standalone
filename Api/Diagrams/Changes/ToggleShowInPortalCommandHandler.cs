using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class ToggleShowInPortalCommandHandler : CommandHandler<ToggleShowInPortalCommand>
{
    protected override async Task Do(ToggleShowInPortalCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(n => n.Id == command.Id);
        
        diagram.ShowInPortal = command.NewShowInPortal;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }

    protected override async Task Undo(ToggleShowInPortalCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(n => n.Id == command.Id);
        
        diagram.ShowInPortal = command.OldShowInPortal;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
}