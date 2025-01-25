using System.Numerics;

namespace BlazorApp.Shared
{
    public  struct DiagramPosition
    {
        public Vector2 Coordinates { get; set; }

        public DiagramPosition(Vector2 coordinates)
        {
            Coordinates = coordinates;
        }
        
        public DiagramPosition(DiagramPosition position)
        {
            Coordinates = position.Coordinates;
        }

    }
}