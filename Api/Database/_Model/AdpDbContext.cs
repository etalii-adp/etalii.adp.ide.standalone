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
        
        // configurationBuilder
        //     .Properties<DiagramPosition>()
        //     .HaveConversion<DiagramPositionToPointConverter>();
    }
}