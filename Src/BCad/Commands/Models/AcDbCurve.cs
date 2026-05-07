#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.DatabaseServices;
#else
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
#endif
#endregion

namespace BcToolsC.BCad.Commands.Models
{
    public readonly struct AcDbCurve
    {
        public readonly Extents3d Bounds;
        public readonly Point3dCollection Vertices;
        public readonly bool Closed;
        public readonly bool ReallyClosing;
        public AcDbCurve(Extents3d _bounds, Point3dCollection _vertices, bool _closed, bool _reallyClosing)
        {
            Bounds = _bounds;
            Vertices = _vertices;
            Closed = _closed;
            ReallyClosing = _reallyClosing;
        }
    }
}