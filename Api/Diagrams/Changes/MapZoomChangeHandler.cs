using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class MapZoomChangeHandler : ChangeHandler<MapZoomChange>
{
    protected override async Task Apply(MapZoomChange change, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == change.Id);

        // Apply changes.
        diagram.Zoom = change.NewZoom;
        diagram.ModificationDate = DateTime.UtcNow;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
    
    protected override async Task Undo(MapZoomChange change, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == change.Id);

        // Apply changes.
        diagram.Zoom = change.OldZoom;
        diagram.ModificationDate = DateTime.UtcNow;
        
        // Tag for modification.
        context.Entry(diagram).State = EntityState.Modified;
    }
}