using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class TagAddCommandHandler : CommandHandler<TagAddCommand>
{
    protected override Task Do(TagAddCommand command, AdpDbContext context)
    {
        return Do(context, command.TagGroupId, command.NewTagId, command.NewTagName);
    }

    public async Task Do(AdpDbContext context, TagGroupIdentifier tagGroupId, TagIdentifier tagId, string tagName)
    {
        // Fetch the tag group.
        var tagGroup = context.ChangeTracker
            .Entries<TagGroup>()
            .SingleOrDefault(n => n.State is EntityState.Added or EntityState.Modified && n.Entity.Id == tagGroupId)?.Entity;

        tagGroup ??= await context.TagGroups
            .Include(n => n.Tags)
            .SingleAsync(n => n.Id == tagGroupId);

        // Apply changes.
        var tag = new Tag
        {
            // TagGroups = [tagGroup],
            Id = tagId,
            Name = tagName,
        };
        tag.TagGroups.Add(tagGroup);
        tagGroup.Tags.Add(tag);
        
        // Tag for modification and addition.
        context.Entry(tagGroup).State = context.Entry(tagGroup).State == EntityState.Added ? EntityState.Added : EntityState.Modified;
        context.Entry(tag).State = EntityState.Added;
    }

    protected override Task Undo(TagAddCommand command, AdpDbContext context)
    {
        return Undo(context, command.TagGroupId, command.NewTagId);
    }

    public async Task Undo(AdpDbContext context, TagGroupIdentifier tagGroupId, TagIdentifier tagId)
    {
        // Fetch the node.
        var tagGroup = await context.TagGroups
            .Include(n => n.Tags)
            .SingleAsync(n => n.Id == tagGroupId);
        
        var tag = tagGroup.Tags.Single(g => g.Id == tagId);

        // Tag for deletion.
        context.Entry(tagGroup).State = EntityState.Modified;
        context.Entry(tag).State = EntityState.Deleted;
    }
}