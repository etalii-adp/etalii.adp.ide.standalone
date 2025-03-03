namespace EtAlii.Adp
{
    public class Tag : IComparable<Tag>
    {
        public required TagIdentifier Id { get; init; }

        public required string Name { get; set; } = string.Empty;
        // public Diagram Diagram { get; set; } = null!;

        public TagGroup TagGroup { get; set; } = null!;

        public int CompareTo(Tag other) => string.Compare(Name, other.Name, StringComparison.Ordinal);
    }
}