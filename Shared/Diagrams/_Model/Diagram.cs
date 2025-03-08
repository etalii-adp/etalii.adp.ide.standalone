using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace EtAlii.Adp;

public record Diagram
{
    public required DiagramIdentifier Id { get; init; }
    
    [XmlIgnore] public required User Owner { get; set; } = null!;
    public required string Name { get; set; } = string.Empty;
    public required string Description { get; set; } = string.Empty;

    [JsonIgnore] public ICollection<Node> Nodes => _nodes;
    // ReSharper disable once InconsistentNaming
    // Reason: We need to still have a property to be able to serialize.
    [JsonInclude, JsonPropertyName(nameof(Nodes))]
    private ICollection<Node> _nodes { get; set; } = new List<Node>();

    [JsonIgnore] public ICollection<Link> Links => _links;
    // ReSharper disable once InconsistentNaming
    // Reason: We need to still have a property to be able to serialize.
    [JsonInclude, JsonPropertyName(nameof(Links))]
    private ICollection<Link> _links { get; set; } = new List<Link>();
    
    public DiagramPosition Position
    {
        get { _position ??= new DiagramPosition { X = _diagramPositionX, Y = _diagramPositionY }; return _position!.Value; }
        set { _position = value; _diagramPositionX = value.X; _diagramPositionY = value.Y; }
    }
    [JsonIgnore] private DiagramPosition? _position;
    [JsonIgnore] private double _diagramPositionX;
    [JsonIgnore] private double _diagramPositionY;

    public required DateTime CreationDate { get; init; }
    public required DateTime ModificationDate { get; set; }
    
    public bool ShowProperties { get; set; } = true;
    public bool ShowNavigation { get; set; } = true;
    
    /// <summary>
    /// Default zoom is 1f.
    /// </summary>
    public required double Zoom { get; set; } = 1f;

    public void Initialize(Diagram diagram)
    {
        _nodes = diagram.Nodes;
        _links = diagram.Links;
    }
}