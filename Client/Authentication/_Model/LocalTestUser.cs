namespace EtAlii.Adp.Client;

public class LocalTestUser
{
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required string Guid { get; init; }

    public static readonly LocalTestUser[] All =
    [
#if DEBUG        
        new()
        {
            Username = "admin",
            Email = "admin@admin.com",
            Guid = "00000000-0000-0000-0000-000000000001"
        },
        new()
        {
            Username = "peter.vrenken",
            Email = "user@user.com",
            Guid = "00000000-0000-0000-0000-000000000002"
        },
        new()
        {
            Username = "tanja.vrenken",
            Email = "user2@user.com",
            Guid = "00000000-0000-0000-0000-000000000003"
        },
        new()
        {
            Username = "arjan.vrenken",
            Email = "user3@user.com",
            Guid = "00000000-0000-0000-0000-000000000004"
        },
        new()
        {
            Username = "ida.vrenken",
            Email = "user4@user.com",
            Guid = "00000000-0000-0000-0000-000000000005"
        }
        #endif
    ];
}