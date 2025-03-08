using System.Text.Json.Serialization;

namespace EtAlii.Adp
{
    public class TagGroup
    {
        public required TagGroupIdentifier Id { get; init; }

        public required string Name { get; set; } = string.Empty;
        // public Diagram Diagram { get; set; } = null!;

        public required TagGroupMode Mode { get; init; }
        public Node? Node { get; set; }
        public Link? Link { get; set; }

        public required int Order { get; init; }
        
        [JsonIgnore] public ICollection<Tag> Tags => _tags;
        // ReSharper disable once InconsistentNaming
        // Reason: We need to still have a property to be able to serialize.
        [JsonInclude, JsonPropertyName(nameof(Tags))]
        private ICollection<Tag> _tags { get; set; } = new List<Tag>();
    }
}