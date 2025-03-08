using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class DiagramZoomCommandHandler : CommandHandler<DiagramZoomCommand>
{
    protected override async Task Do(DiagramZoomCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == command.Id);

        // Apply changes.
        diagram.Zoom = command.NewZoom;
        diagram.ModificationDate = DateTime.UtcNow;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
    
    protected override async Task Undo(DiagramZoomCommand command, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == command.Id);

        // Apply changes.
        diagram.Zoom = command.OldZoom;
        diagram.ModificationDate = DateTime.UtcNow;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
}