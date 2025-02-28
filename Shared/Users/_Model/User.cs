namespace EtAlii.Adp;

public class User
{
    public required UserIdentifier Id { get; init; }
    public required string Name { get; init; }
    public required DateTime JoinDate { get; init; }
    public required string ExternalIdentifier { get; init; }

    public required Theme Theme { get; set; } = Theme.Light;
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
            Theme = Theme.Light,
        },
        new()
        {
            Id = (UserIdentifier)Guid.Parse("00000000-0000-0000-0000-000000000002"),
            JoinDate = DateTime.Now.Subtract(TimeSpan.FromHours(24)),
            Name = "peter.vrenken",
            ExternalIdentifier = "github-peter",
            Theme = Theme.Light,
        },
        new()
        {
            Id = (UserIdentifier)Guid.Parse("3618849b-7086-4dea-9531-87476faaa340"),
            JoinDate = DateTime.Now.Subtract(TimeSpan.FromHours(24)),
            Name = "vrenken",
            ExternalIdentifier = "ed88e2ea385240d8a2249627eaf1bcd6@github",
            Theme = Theme.Light,
        },
        new()
        {
            Id = (UserIdentifier)Guid.Parse("64164a21-c899-4873-b078-939af5f15d1b"),
            JoinDate = DateTime.Now.Subtract(TimeSpan.FromHours(24)),
            Name = "vrenken@live.nl",
            ExternalIdentifier = "5072f74699fd4a3e8f40161ba787cbdb@aad",
            Theme = Theme.Light,
        },
        new()
        {
            Id = (UserIdentifier)Guid.Parse("00000000-0000-0000-0000-000000000003"),
            JoinDate = DateTime.Now.Subtract(TimeSpan.FromHours(879)),
            Name = "tanja.vrenken",
            ExternalIdentifier = "github-tanja",
            Theme = Theme.Dark,
        },
        new()
        {
            Id = (UserIdentifier)Guid.Parse("00000000-0000-0000-0000-000000000004"),
            JoinDate = DateTime.Now.Subtract(TimeSpan.FromHours(8765)),
            Name = "arjan.vrenken",
            ExternalIdentifier = "aad-arjan-00000000-0000-0000-0000-000000000004",
            Theme = Theme.Auto,
        },
        new()
        {
            Id = (UserIdentifier)Guid.Parse("00000000-0000-0000-0000-000000000005"),
            JoinDate = DateTime.Now.Subtract(TimeSpan.FromHours(3445)),
            Name = "ida.vrenken",
            ExternalIdentifier = "aad-ida-00000000-0000-0000-0000-000000000005",
            Theme = Theme.Light,
        }
#endif
    ];
}