using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class ToggleShowPropertiesCommandHandler : CommandHandler<ToggleShowPropertiesCommand>
{
    protected override async Task Do(ToggleShowPropertiesCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(n => n.Id == command.Id);
        
        diagram.ShowProperties = command.NewShowProperties;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }

    protected override async Task Undo(ToggleShowPropertiesCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(n => n.Id == command.Id);
        
        diagram.ShowProperties = command.OldShowProperties;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
}