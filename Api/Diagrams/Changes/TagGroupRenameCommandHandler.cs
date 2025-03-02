using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class TagGroupRenameCommandHandler : CommandHandler<TagGroupRenameCommand>
{
    protected override async Task Do(TagGroupRenameCommand command, AdpDbContext context)
    {
        // Fetch the node.
        var tagGroup = await context.TagGroups.SingleAsync(n => n.Id == command.TagGroupId);
        
        tagGroup.Name = command.NewName;
        
        // Tag for modification.
        context.Entry(tagGroup).State = EntityState.Modified;
    }
    
    protected override async Task Undo(TagGroupRenameCommand command, AdpDbContext context)
    {
        // Fetch the node.
        var tagGroup = await context.TagGroups.SingleAsync(n => n.Id == command.TagGroupId);
        
        tagGroup.Name = command.OldName;
        
        // Tag for modification.
        context.Entry(tagGroup).State = EntityState.Modified;
    }
}