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
            
        // configurationBuilder
        //     .Properties<DiagramPosition>()
        //     .HaveConversion<DiagramPositionToPointConverter>();
    }
}