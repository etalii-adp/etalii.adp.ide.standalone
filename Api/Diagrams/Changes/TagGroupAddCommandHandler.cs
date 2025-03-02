using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class TagGroupAddCommandHandler : CommandHandler<TagGroupAddCommand>
{
    protected override Task Do(TagGroupAddCommand command, AdpDbContext context)
    {
        return Do(context, command.NodeId, command.NewTagGroupId, command.NewTagGroupName);
    }

    public async Task Do(AdpDbContext context, NodeIdentifier nodeId, TagGroupIdentifier tagGroupId, string tagGroupName)
    {
        // Fetch the node.
        var node = context.ChangeTracker
            .Entries<Node>()
            .SingleOrDefault(n => n.State is EntityState.Added or EntityState.Modified && n.Entity.Id == nodeId)?.Entity;

        node ??= await context.Nodes
            .Include(n => n.TagGroups)
            .SingleAsync(n => n.Id == nodeId);

        // Apply changes.
        var group = new TagGroup
        {
            Node = node,
            Id = tagGroupId,
            Name = tagGroupName,
        };
        node.TagGroups.Add(group);
        
        // Tag for modification and addition.
        context.Entry(node).State = context.Entry(node).State == EntityState.Added ? EntityState.Added : EntityState.Modified;
        context.Entry(group).State = EntityState.Added;
    }

    protected override Task Undo(TagGroupAddCommand command, AdpDbContext context)
    {
        return Undo(context, command.NodeId, command.NewTagGroupId);
    }

    public async Task Undo(AdpDbContext context, NodeIdentifier nodeId, TagGroupIdentifier tagGroupId)
    {
        // Fetch the node.
        var node = await context.Nodes
            .Include(n => n.TagGroups)
            .SingleAsync(n => n.Id == nodeId);
        
        var group = node.TagGroups.Single(g => g.Id == tagGroupId);

        // Tag for deletion.
        context.Entry(node).State = EntityState.Modified;
        context.Entry(group).State = EntityState.Deleted;
    }
}