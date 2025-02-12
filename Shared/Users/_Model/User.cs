namespace EtAlii.Adp;

public class User
{
    public required UserIdentifier Id { get; init; }
    public required string Name { get; init; }
    public required DateTime JoinDate { get; init; }
    public required string ExternalIdentifier { get; init; }

    public ICollection<Diagram> Diagrams { get; private set; } = new List<Diagram>();
    
    public static readonly User[] LocalTestUsers =
    [
#if DEBUG        
        new()
        {
            Id = (UserIdentifier)Guid.Parse("00000000-0000-0000-0000-000000000001"),
            JoinDate = DateTime.Now,
            Name = "admin",
            ExternalIdentifier = "github-admin-00000000-0000-0000-0000-000000000001",
        },
        new()
        {
            Id = (UserIdentifier)Guid.Parse("00000000-0000-0000-0000-000000000002"),
            JoinDate = DateTime.Now.Subtract(TimeSpan.FromHours(24)),
            Name = "peter.vrenken",
            ExternalIdentifier = "github-peter",
        },
        new()
        {
            Id = (UserIdentifier)Guid.Parse("00000000-0000-0000-0000-000000000003"),
            JoinDate = DateTime.Now.Subtract(TimeSpan.FromHours(879)),
            Name = "tanja.vrenken",
            ExternalIdentifier = "github-tanja",
        },
        new()
        {
            Id = (UserIdentifier)Guid.Parse("00000000-0000-0000-0000-000000000004"),
            JoinDate = DateTime.Now.Subtract(TimeSpan.FromHours(8765)),
            Name = "arjan.vrenken",
            ExternalIdentifier = "aad-arjan-00000000-0000-0000-0000-000000000004",
        },
        new()
        {
            Id = (UserIdentifier)Guid.Parse("00000000-0000-0000-0000-000000000005"),
            JoinDate = DateTime.Now.Subtract(TimeSpan.FromHours(3445)),
            Name = "ida.vrenken",
            ExternalIdentifier = "aad-ida-00000000-0000-0000-0000-000000000005",
        }
#endif
    ];
}