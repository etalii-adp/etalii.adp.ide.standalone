using System.Text.Json.Serialization;

namespace EtAlii.Adp;

public class Link
{
    public required LinkIdentifier Id { get; init; }
    public Diagram Diagram { get; set; } = null!;
    public required Node SourceNode { get; init; }
    public required string SourcePort { get; init; }
    public required Node TargetNode { get; init; }
    public required string TargetPort { get; init; }
    
    [JsonIgnore] public ICollection<TagGroup> TagGroups => _tagGroups;
    // ReSharper disable once InconsistentNaming
    // Reason: We need to still have a property to be able to serialize.
    [JsonInclude, JsonPropertyName(nameof(TagGroups))]
    private ICollection<TagGroup> _tagGroups { get; set; } = new List<TagGroup>();
}