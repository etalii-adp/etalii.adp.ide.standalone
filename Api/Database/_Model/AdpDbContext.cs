using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class AdpDbContext : DbContext
{
    /// <summary>
    /// The diagrams stored in the system.
    /// </summary>
    public DbSet<Diagram> Diagrams { get; set; }

    // ReSharper disable once ConvertToPrimaryConstructor
    // Reason: Needs to be public so that the contexts can be pooled. 
    public AdpDbContext(DbContextOptions<AdpDbContext> options) : base(options) { }
    
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<Diagram>();

        builder.HasIndex(e => e.Id);
        
        builder
            .Property(e => e.Id)
            .IsRequired();

        builder
            .Property(e => e.Position)
            .IsRequired();
    }
    
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<DiagramIdentifier>()
            .HaveConversion<DiagramIdentifierToGuidConverter>();
        
        configurationBuilder
            .Properties<DiagramPosition>()
            .HaveConversion<DiagramPositionToPointConverter>();
    }
}