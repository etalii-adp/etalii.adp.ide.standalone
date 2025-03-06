using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class TagAssignCommandHandler : CommandHandler<TagAssignCommand>
{
    protected override Task Do(TagAssignCommand command, AdpDbContext context)
    {
        return Do(context, command.TagGroupId, command.TagId);
    }

    public async Task Do(AdpDbContext context, TagGroupIdentifier tagGroupId, TagIdentifier tagId)
    {
        // Fetch the tag group.
        var tagGroup = context.ChangeTracker
            .Entries<TagGroup>()
            .SingleOrDefault(n => n.State is EntityState.Added or EntityState.Modified && n.Entity.Id == tagGroupId)?.Entity;

        tagGroup ??= await context.TagGroups
            .Include(n => n.Tags)
            .SingleAsync(n => n.Id == tagGroupId);

        var tag = context.ChangeTracker
            .Entries<Tag>()
            .SingleOrDefault(n => n.State is EntityState.Added or EntityState.Modified && n.Entity.Id == tagId)?.Entity;

        tag ??= await context.Tags
            .SingleAsync(n => n.Id == tagId);

        tag.TagGroups.Add(tagGroup);
        tagGroup.Tags.Add(tag);
        
        // Tag for modification and addition.
        context.Entry(tagGroup).State = context.Entry(tagGroup).State == EntityState.Added ? EntityState.Added : EntityState.Modified;
        context.Entry(tag).State = context.Entry(tag).State == EntityState.Added ? EntityState.Added : EntityState.Modified;
    }

    protected override Task Undo(TagAssignCommand command, AdpDbContext context)
    {
        return Undo(context, command.TagGroupId, command.TagId);
    }

    public async Task Undo(AdpDbContext context, TagGroupIdentifier tagGroupId, TagIdentifier tagId)
    {
        // Fetch the tag group.
        var tagGroup = context.ChangeTracker
            .Entries<TagGroup>()
            .SingleOrDefault(n => n.State is EntityState.Added or EntityState.Modified && n.Entity.Id == tagGroupId)?.Entity;

        tagGroup ??= await context.TagGroups
            .Include(n => n.Tags)
            .SingleAsync(n => n.Id == tagGroupId);
        
        var tag = tagGroup.Tags.Single(g => g.Id == tagId);
        tagGroup.Tags.Remove(tag);
        tag.TagGroups.Remove(tagGroup);
        
        // Tag for deletion.
        context.Entry(tagGroup).State = EntityState.Modified;
        context.Entry(tag).State = EntityState.Modified;
    }
}