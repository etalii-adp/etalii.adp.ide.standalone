namespace EtAlii.Adp
{
    public class TagGroup
    {
        public required TagGroupIdentifier Id { get; init; }

        public string Name { get; set; } = string.Empty;
        public Diagram Diagram { get; set; } = null!;
    }
}