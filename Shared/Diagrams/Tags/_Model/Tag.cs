namespace EtAlii.Adp
{
    public class Tag
    {
        public required TagIdentifier Id { get; init; }

        public required string Name { get; set; } = string.Empty;
        // public Diagram Diagram { get; set; } = null!;

        public TagGroup TagGroup { get; set; } = null!;
    }
}