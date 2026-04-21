using System; // Keep for .NET 4.6
using System.Collections.Generic;

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
        // Polyline
        public struct BulgeVertex2d : IFormattable
        {
            public readonly Point2d point;
            public readonly double bulge;
            public BulgeVertex2d(Point2d point, double bulge)
            {
                this.point = point;
                this.bulge = bulge;
            }

            public string ToString(string format, IFormatProvider formatProvider)
            {
                object[] array = null;
                try
                {
                    array = new object[2];
                    double num2 = point.X;
                    array[0] = num2.ToString(format, formatProvider);
                    double num3 = point.Y;
                    array[1] = num3.ToString(format, formatProvider);
                    return string.Format("({0},{1})", array);
                }
                catch
                {
                    return $"({point.X},{point.Y})";
                }
            }
        }

        public Polyline AddLWPolyline<T>(IEnumerable<T> vertexes,
            string linetype = "Continuous", double linetypeWidth = 0, double linetypeScale = 1.0, bool linetypeGeneration = false,
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
                        case BulgeVertex2d k:
                            entity.AddVertexAt(i, k.point, k.bulge, linetypeWidth, linetypeWidth);
                            i++;
                            break;
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