using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class MapPositionChangeHandler : ChangeHandler<DiagramPositionChange>
{
    protected override async Task Apply(DiagramPositionChange change, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == change.Id);
        
        // Apply changes.
        diagram.Position = change.NewPosition;
        diagram.ModificationDate = DateTime.UtcNow;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
    protected override async Task Undo(DiagramPositionChange change, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == change.Id);

        // Apply changes.
        diagram.Position = change.OldPosition;
        diagram.ModificationDate = DateTime.UtcNow;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
}