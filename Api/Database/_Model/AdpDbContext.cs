using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EtAlii.Adp.Api;

public class AdpDbContext : DbContext
{
    /// <summary>
    /// The diagrams stored in the system.
    /// </summary>
    public DbSet<Diagram> Diagrams { get; set; }

    public DbSet<User> Users { get; set; }
    
    /// <summary>
    /// The nodes that are known to the system.
    /// </summary>
    public DbSet<Node> Nodes { get; set; }

    /// <summary>
    /// The links that are connecting the nodes in a diagram.
    /// </summary>
    public DbSet<Link> Links { get; set; }

    public DbSet<Tag> Tags { get; set; }
    
    public DbSet<TagGroup> TagGroups { get; set; }
    
    // ReSharper disable once ConvertToPrimaryConstructor
    // Reason: Needs to be public so that the contexts can be pooled. 
    public AdpDbContext(DbContextOptions<AdpDbContext> options) : base(options) { }
    
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Users.
        Configure(modelBuilder.Entity<User>(), builder =>
        {
            builder.HasIndex(e => e.Id);
            builder
                .Property(e => e.Id)
                .IsRequired();

            builder
                .Property(e => e.Name)
                .IsRequired();

            builder
                .Property(e => e.JoinDate)
                .IsRequired();

            builder
                .Property(e => e.ExternalIdentifier)
                .IsRequired();

            builder
                .HasMany(e => e.Diagrams)
                .WithOne(e => e.Owner)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
        
        // Diagrams.
        Configure(modelBuilder.Entity<Diagram>(), builder =>
        {
            builder.HasIndex(e => e.Id);
            builder
                .Property(e => e.Id)
                .IsRequired();

            builder
                .Property(e => e.Name)
                .IsRequired();

            builder
                .Property(e => e.Zoom)
                .IsRequired();

            builder
                .Ignore(e => e.Position);

            builder
                .Ignore(e => e.IsReadOnly);

            builder
                .Property<double>("_diagramPositionX")
                .HasColumnName("DiagramPositionX")
                .IsRequired();
            builder
                .Property<double>("_diagramPositionY")
                .HasColumnName("DiagramPositionY")
                .IsRequired();
            
            builder
                .HasMany(e => e.Nodes)
                .WithOne(e => e.Diagram)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            builder
                .HasMany(e => e.Links)
                .WithOne(e => e.Diagram)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
        
        // Nodes.
        Configure(modelBuilder.Entity<Node>(), builder =>
        {
            builder.HasIndex(e => e.Id);
            builder
                .Property(e => e.Id)
                .IsRequired();

            builder
                .Property(e => e.Name)
                .IsRequired();

            builder
                .Ignore(e => e.Position);
            builder
                .Property<double>("_nodePositionX")
                .HasColumnName("NodePositionX")
                .IsRequired();
            builder
                .Property<double>("_nodePositionY")
                .HasColumnName("NodePositionY")
                .IsRequired();
            
            builder
                .HasMany(e => e.TagGroups)
                .WithOne(o => o.Node)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Links.
        Configure(modelBuilder.Entity<Link>(), builder =>
        {
            builder.HasIndex(e => e.Id);
            builder
                .Property(e => e.Id)
                .IsRequired();

            builder
                .Property(e => e.SourcePort)
                .IsRequired();

            builder
                .Property(e => e.TargetPort)
                .IsRequired();

            builder
                .HasOne(e => e.SourceNode)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            
            builder
                .HasOne(e => e.TargetNode)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            
            builder
                .HasMany(e => e.TagGroups)
                .WithOne(o => o.Link)
                .OnDelete(DeleteBehavior.Cascade);
        });

        Configure(modelBuilder.Entity<TagGroup>(), builder =>
        {
            builder.HasIndex(e => e.Id);
            builder
                .Property(e => e.Id)
                .IsRequired();

            builder
                .Property(e => e.Name)
                .IsRequired();

            builder
                .HasMany(e => e.Tags)
                .WithMany(o => o.TagGroups);
                //.OnDelete(DeleteBehavior.Cascade)
                //.IsRequired();

            builder
                .HasOne(e => e.Link)
                .WithMany(o => o.TagGroups)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne(e => e.Node)
                .WithMany(o => o.TagGroups)
                .OnDelete(DeleteBehavior.Restrict);
        });
        
        Configure(modelBuilder.Entity<Tag>(), builder =>
        {
            builder.HasIndex(e => e.Id);
            builder
                .Property(e => e.Id)
                .IsRequired();

            builder
                .Property(e => e.Name)
                .IsRequired();
        });
    }

    private void Configure<TEntity>(EntityTypeBuilder<TEntity> builder, Action<EntityTypeBuilder<TEntity>> configure) 
        where TEntity : class => configure(builder);        
    
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<DiagramIdentifier>()
            .HaveConversion<DiagramIdentifierToGuidConverter>();

        configurationBuilder
            .Properties<UserIdentifier>()
            .HaveConversion<UserIdentifierToGuidConverter>();

        configurationBuilder
            .Properties<NodeIdentifier>()
            .HaveConversion<NodeIdentifierToGuidConverter>();

        configurationBuilder
            .Properties<LinkIdentifier>()
            .HaveConversion<LinkIdentifierToGuidConverter>();

        configurationBuilder
            .Properties<LinkIdentifier>()
            .HaveConversion<LinkIdentifierToGuidConverter>();

        configurationBuilder
            .Properties<TagIdentifier>()
            .HaveConversion<TagIdentifierToGuidConverter>();

        configurationBuilder
            .Properties<TagGroupIdentifier>()
            .HaveConversion<TagGroupIdentifierToGuidConverter>();

        // configurationBuilder
        //     .Properties<DiagramPosition>()
        //     .HaveConversion<DiagramPositionToPointConverter>();
    }
}