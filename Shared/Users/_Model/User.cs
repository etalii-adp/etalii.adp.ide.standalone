namespace EtAlii.Adp;

public class User
{
    public required UserIdentifier Id { get; init; }
    public required string Name { get; set; }
    public required DateTime JoinDate { get; set; }
    
    public ICollection<Diagram> Diagrams { get; private set; } = new List<Diagram>();

}