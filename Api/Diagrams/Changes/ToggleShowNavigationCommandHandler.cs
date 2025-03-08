using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class ToggleShowNavigationCommandHandler : CommandHandler<ToggleShowNavigationCommand>
{
    protected override async Task Do(ToggleShowNavigationCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(n => n.Id == command.Id);
        
        diagram.ShowNavigation = command.NewShowNavigation;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }

    protected override async Task Undo(ToggleShowNavigationCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(n => n.Id == command.Id);
        
        diagram.ShowNavigation = command.OldShowNavigation;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
}