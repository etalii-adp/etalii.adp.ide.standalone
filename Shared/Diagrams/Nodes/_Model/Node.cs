using System.Text.Json.Serialization;

namespace EtAlii.Adp
{
    public class Node
    {
        public required NodeIdentifier Id { get; init; }
        
        public required Diagram Diagram { get; init; }

        public NodePosition Position
        {
            get { if(_position == null) _position = new NodePosition { X = _nodePositionX, Y = _nodePositionY }; return _position; }
            set { _position = value; _nodePositionX = value.X; _nodePositionY = value.Y; }
        }
        [JsonIgnore] private NodePosition? _position;
        [JsonIgnore] private double _nodePositionX;
        [JsonIgnore] private double _nodePositionY;
    }
}