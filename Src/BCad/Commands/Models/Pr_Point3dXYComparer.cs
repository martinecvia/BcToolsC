using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using ZwSoft.ZwCAD.Geometry;
#else
using Autodesk.AutoCAD.Geometry;
#endif
#endregion

namespace BcToolsC.BCad.Commands.Models
{
    public sealed class Pr_Point3dXYComparer 
        : IEqualityComparer<Point3d>
    {
        readonly double _tolerance;
        public Pr_Point3dXYComparer(double tolerance = 1E-5) { _tolerance = tolerance; }
        public bool Equals(Point3d a, Point3d b)
            => Math.Abs(a.X - b.X) < _tolerance
            && Math.Abs(a.Y - b.Y) < _tolerance;
        private long Q(double value) => (long)Math.Round(value / _tolerance);
        public int GetHashCode(Point3d p) => (Q(p.X).GetHashCode() * 397) ^ Q(p.Y).GetHashCode();
    }
}