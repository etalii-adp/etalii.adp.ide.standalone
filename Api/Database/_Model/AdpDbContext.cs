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
            .HasConversion<DiagramIdentifierToGuidConverter>();
        
        builder
            .Property(e => e.Position)
            .HasConversion<DiagramPositionToPointConverter>();

    }
    
    // protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    // {
    //     // Apply a global conversion for DateTime -> long (Unix timestamp)
    //     configurationBuilder
    //         .Properties<DiagramIdentifier>()
    //         .HaveConversion<DiagramIdentifierToGuidConverter>();
    //     
    //     configurationBuilder
    //         .Properties<DiagramPosition>()
    //         .HaveConversion<DiagramPositionToPointConverter>();
    // }
    
// using var dbContext = moduleDefinition.GetDbContext(scope);
// if (dbContext.Database.GetPendingMigrations().Any())
// {
//     _logger.Debug("Migrating database for {ModuleName}", moduleDefinition.GetType().Name);
//     dbContext.Database.Migrate();
// }
// else
// {
//     _logger.Debug("Database is in sync with {ModuleName}", moduleDefinition.GetType().Name);
// }

}