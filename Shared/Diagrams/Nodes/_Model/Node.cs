using System.Text.Json.Serialization;

namespace EtAlii.Adp
{
    public class Node
    {
        public required NodeIdentifier Id { get; init; }

        public Diagram Diagram { get; set; } = null!;

        public NodePosition Position
        {
            get { if(_position == null) _position = new NodePosition { X = _nodePositionX, Y = _nodePositionY }; return _position; }
            set { _position = value; _nodePositionX = value.X; _nodePositionY = value.Y; }
        }
        [JsonIgnore] private NodePosition? _position;
        [JsonIgnore] private double _nodePositionX;
        [JsonIgnore] private double _nodePositionY;
        
        
        [JsonIgnore] public ICollection<Link> InboundLinks => _inboundLinks;
        // ReSharper disable once InconsistentNaming
        // Reason: We need to still have a property to be able to serialize.
        [JsonInclude, JsonPropertyName(nameof(InboundLinks))]
        private ICollection<Link> _inboundLinks { get; set; } = new List<Link>();

        [JsonIgnore] public ICollection<Link> OutboundLinks => _outboundLinks;
        // ReSharper disable once InconsistentNaming
        // Reason: We need to still have a property to be able to serialize.
        [JsonInclude, JsonPropertyName(nameof(OutboundLinks))]
        private ICollection<Link> _outboundLinks { get; set; } = new List<Link>();

    }
}