using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class MapPositionCommandHandler : CommandHandler<DiagramPositionCommand>
{
    protected override async Task Do(DiagramPositionCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == command.Id);
        
        // Apply changes.
        diagram.Position = command.NewPosition;
        diagram.ModificationDate = DateTime.UtcNow;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
    protected override async Task Undo(DiagramPositionCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == command.Id);

        // Apply changes.
        diagram.Position = command.OldPosition;
        diagram.ModificationDate = DateTime.UtcNow;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
}