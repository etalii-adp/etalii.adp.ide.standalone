using System.Text.Json.Serialization;

namespace EtAlii.Adp
{
    public class TagGroup
    {
        public required TagGroupIdentifier Id { get; init; }

        public required string Name { get; set; } = string.Empty;
        // public Diagram Diagram { get; set; } = null!;

        public Node Node { get; init; } = null!;
        public Link Link { get; init; } = null!;
        
        [JsonIgnore] public ICollection<Tag> Tags => _tags;
        // ReSharper disable once InconsistentNaming
        // Reason: We need to still have a property to be able to serialize.
        [JsonInclude, JsonPropertyName(nameof(Tags))]
        private ICollection<Tag> _tags { get; set; } = new List<Tag>();

    }
}