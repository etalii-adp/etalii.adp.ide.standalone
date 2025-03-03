using System.Text.Json.Serialization;

namespace EtAlii.Adp
{
    public class Tag : IComparable<Tag>
    {
        public required TagIdentifier Id { get; init; }

        public required string Name { get; set; } = string.Empty;
        // public Diagram Diagram { get; set; } = null!;

        [JsonIgnore] public ICollection<TagGroup> TagGroups => _tagGroups;
        // ReSharper disable once InconsistentNaming
        // Reason: We need to still have a property to be able to serialize.
        [JsonInclude, JsonPropertyName(nameof(TagGroups))]
        private ICollection<TagGroup> _tagGroups { get; set; } = new List<TagGroup>();

        public int CompareTo(Tag other) => string.Compare(Name, other.Name, StringComparison.Ordinal);
    }
}