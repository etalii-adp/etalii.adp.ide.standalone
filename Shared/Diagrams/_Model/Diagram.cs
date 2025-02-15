using System.Text.Json.Serialization;

namespace EtAlii.Adp;

public record Diagram
{
    public required DiagramIdentifier Id { get; init; }
    public required User Owner { get; set; } = null!;
    public required string Name { get; set; } = string.Empty;
    public required string Description { get; init; }

    [JsonIgnore]
    public ICollection<Node> Nodes => _nodes;
    // ReSharper disable once InconsistentNaming
    // Reason: We need to still have a property to be able to serialize.
    [JsonInclude, JsonPropertyName("Nodes")]
    private ICollection<Node> _nodes { get; set; } = new List<Node>();
    
    public DiagramPosition Position
    {
        get { if(_position == null) _position = new DiagramPosition { X = _diagramPositionX, Y = _diagramPositionY }; return _position; }
        set { _position = value; _diagramPositionX = value.X; _diagramPositionY = value.Y; }
    }
    [JsonIgnore] private DiagramPosition? _position;
    [JsonIgnore] private double _diagramPositionX;
    [JsonIgnore] private double _diagramPositionY;

    public required DateTime CreationDate { get; init; }
    public required DateTime ModificationDate { get; set; }
    
    /// <summary>
    /// Default zoom is 1f.
    /// </summary>
    public required double Zoom { get; set; } = 1f; 
}