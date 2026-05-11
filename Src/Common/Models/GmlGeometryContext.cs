using System; // Keep for .NET 4.6

namespace BcToolsC.Models
{

    [Flags]
    public enum GmlGeometryContext
        : short
    {
        None = 0,                // 000000
        Polygon = 1 << 0,        // 000001, gml:Polygon
        exterior = 1 << 1,       // 000010, gml:exterior
        LinearRing = 1 << 2,     // 000100, gml:LinearRing
        referencePoint = 1 << 3, // 001000, cp:referencePoint / pai:DefinicniBod
        Point = 1 << 4           // 010000, gml:Point
    }
}