using System.Collections.Generic; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM 
#if ZWCAD
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;
#else
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
#endif
#endregion

using BcToolsC.Models;

namespace BcToolsC.BCad.Transactions
{
    // https://help.autodesk.com/view/OARX/2026/CSY/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Entity
    // https://help.autodesk.com/view/OARX/2026/CSY/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Curve
    public partial class BCadTransaction
    {
        public Polyline AddLWPolyline(Point2d start, Point2d end,
            string linetype = "Continuous", double linetypeWidth = 0.0, double linetypeScale = 1.0, bool linetypeGeneration = false,
            LAYER? layer = null,
            COLOR? color = null,
            ANGLE? angle = null,
            bool shouldBeClosed = false)
            => AddLWPolyline(
#if NET8_0_OR_GREATER
                [start, end],
#else
                new[] { start, end },
#endif
                linetype, linetypeWidth, linetypeScale, linetypeGeneration,
                layer, color, angle, false);

        public Polyline AddLWPolyline<T>(IEnumerable<T> vertexes,
            string linetype = "Continuous", double linetypeWidth = 0.0, double linetypeScale = 1.0, bool linetypeGeneration = false,
            LAYER? layer = null,
            COLOR? color = null,
            ANGLE? angle = null,
            bool shouldBeClosed = false)
        {
            Polyline entity = new Polyline { Closed = shouldBeClosed };
            if (vertexes != null)
            {
                int i = 0;
                foreach (var j in vertexes)
                {
                    switch (j)
                    {
                        case Point2d k:
                            entity.AddVertexAt(i, k, 0.0, linetypeWidth, linetypeWidth);
                            i++;
                            break;
                        case Point3d k:
                            entity.AddVertexAt(i, new Point2d(k.X, k.Y), 0.0, linetypeWidth, linetypeWidth);
                            i++;
                            break;
                    }
                }
                // Otáčíme kolem prvního bodu v listu
                if (angle.HasValue && entity.NumberOfVertices > 0)
                {
                    entity.TransformBy(Matrix3d.Rotation(angle.Value, Vector3d.ZAxis,
                        entity.StartPoint));
                }
            }
            Polyline result = AddToModelSpace(entity);
            result.LayerId = EnsureLayer(layer);
            result.LinetypeId = EnsureLinetype(linetype);
            result.LinetypeScale = linetypeScale;
            result.Plinegen = linetypeGeneration;
            result.Color = EnsureColor(color);
            return result;
        }
    }
}