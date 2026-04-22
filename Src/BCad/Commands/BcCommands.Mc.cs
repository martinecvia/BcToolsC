#pragma warning disable IDE0028, IDE0057, IDE0062, IDE0063, IDE0090, IDE1006
using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.Geometry;
using AcDb = ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using AcBr = ZwSoft.ZwCAD.BoundaryRepresentation;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcBr = Autodesk.AutoCAD.BoundaryRepresentation;
#endif
#endregion

using BcToolsC.BCad.Transactions;
using static BcToolsC.BCad.Transactions.BCadTransaction;
using System.Windows.Controls;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly AcRun.RXClass _proxyEntity = AcRun.RXObject.GetClass(typeof(ProxyEntity));
        readonly AcRun.RXClass _proxyObject = AcRun.RXObject.GetClass(typeof(ProxyObject));

        [AcRun.CommandMethod("BCTOOLSC_MC_RM_PROXY")]
        public void _ClearProxy()
        {
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Editor editor = document.Editor;
            Database database = BcApp.Document.Database;
            long n = database.Handseed.Value / 100L;
            AcRun.ProgressMeter progress = new AcRun.ProgressMeter();
            progress.SetLimit(100);
            progress.Start("Procházím ...");
            // https://forums.autodesk.com/t5/net-forum/proxyobjects-amp-proxyentities-how-to-find-all/td-p/10867012
            var i = 0;
            Call(t => 
            {
                try
                {
                    var h = database.BlockTableId.Handle;
                    var l = h.Value;
                    string GetPromptByRegApp(string regApp)
                    {
                        string[] parts = regApp.Split('|');
                        if (parts.Length < 2)
                            return regApp;
                        if (parts[1].StartsWith("Product Desc: "))
                            return $"{parts[0].TrimStart('"')} ({parts[1].Substring(13).Trim()})";
                        return parts[0].Trim();
                    }
                    while (true)
                    {
                        long p = l;
                        h = database.Handseed;
                        long c = h.Value;
                        if (p >= c) break;
                        if (l % n == 0L) progress.MeterProgress();
                        if (database.TryGetObjectId(new Handle(l), out ObjectId id) && !id.IsErased)
                        {
                            string regApp;
                            if (id.ObjectClass.IsDerivedFrom(_proxyEntity))
                            {
                                // ProxyEntity
                                if (t.TryGet(id, out ProxyEntity e, OpenMode.ForWrite) &&
                                    !e.IsErased &&
                                    (e.ProxyFlags & 1) == 1)
                                {
                                    regApp = GetPromptByRegApp(e.ApplicationDescription);
                                    if (!e.IsWriteEnabled) e.UpgradeOpen();
                                    e.Erase(true);
                                    ++i;
                                }
                            }
                            else if (id.ObjectClass.IsDerivedFrom(_proxyObject))
                            {
                                // ProxyObject
                                if (t.TryGet(id, out ProxyObject o, OpenMode.ForWrite) &&
                                    !o.IsErased &&
                                    (o.ProxyFlags & 1) == 1)
                                {
                                    regApp = GetPromptByRegApp(o.ApplicationDescription);
                                    if (!o.IsWriteEnabled) o.UpgradeOpen();
                                    o.Erase(true);
                                    ++i;
                                }
                            }
                        }
                        ++l;
                    }
                }
                catch (Exception)
                { }
            });
            progress.Stop();
            editor.Info("Smazáno proxy objektů: " + i);
        }

        [AcRun.CommandMethod("BCTOOLSC_MC_PROFILE_SOLID")]
        public void _ProfileWithSolid() => _Profiler(true);
        [AcRun.CommandMethod("BCTOOLSC_MC_PROFILE")]
        public void _ProfileEmpty() => _Profiler();
        void _Profiler(bool promptWithSolid = false)
        {
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Editor editor = document.Editor;
            Matrix3d ucs = editor.CurrentUserCoordinateSystem;
            var __curve = GetEntityFromPrompt(editor, "Vyber(Polyline, Polyline3d)", 
            typeof(Polyline3d), typeof(Polyline2d), typeof(Polyline));
            if (__curve == ObjectId.Null) goto no_data;
            var __point = GetPointFromPrompt(editor, "Vyber vkládací bod");
            if (__point == null) goto user_closed_dialog;
            // Vložení podle uživatelského UCS
            var point = __point.Value.TransformBy(ucs);
            ObjectId __solid = ObjectId.Null;
            if (promptWithSolid)
            {
                __solid = GetEntityFromPrompt(editor, "Vyber(Solid3d, SubDMesh, Surface)",
                typeof(Solid3d), typeof(SubDMesh), typeof(AcDb.Surface));
                if (__solid == ObjectId.Null) goto user_closed_dialog;
            }
            Call(t =>
            {
                if (!t.TryGet(__curve, out Curve curve)) goto local_no_data;
                Point3dCollection vertice = GetPolylineVertices(t, curve);
                if (vertice.Count < 2) goto local_no_data;
                if (curve.Closed) vertice.Add(vertice[0]);
                List<Point2d> pts = _Profiler_CollectVertice(vertice, curve);
                double dx = point.X;
                double dy = point.Y;
                // Srovnávací rovina
                double totalDistance = curve.GetDistanceAtParameter(curve.EndParam);
                t.AddLWPolyline(new Point2d(point.X, point.Y), new Point2d(point.X + totalDistance, point.Y));
                // Vykreslení 
                t.AddLWPolyline(pts.Select(p => new Point2d(p.X + dx, p.Y + dy)));
                if (!promptWithSolid) return;
                if (t.Exists(__solid) && t.TryGet(__solid, out Entity solid))
                {
                    var mts = _Profiler_CollectIntersectsWith(vertice, curve, solid);
                    foreach (var tmp in mts) if (tmp.Count != 0)
                        t.AddLWPolyline(tmp.Select(p => new Point2d(p.X + dx, p.Y + dy)));
                }
                return;
            local_no_data:
                editor.Warn("Nebyli nalazeny žádné data.");
                return;
            });
            return;
        no_data:
            editor.Warn("Nebyli nalazeny žádné data.");
            return;
        user_closed_dialog:
            editor.Warn("Výběr byl zrušen uživatelem mezi monitorem a židlí.");
            return;
        }

        Point3dCollection GetPolylineVertices(BCadTransaction t, Curve curve)
        {
            Point3dCollection result = new Point3dCollection();
            if (curve is Polyline3d poly3d)
            {
                var type = poly3d.PolyType;
                foreach (ObjectId v3dId in poly3d) if (t.Exists(v3dId)
                    && t.TryGet(v3dId, out PolylineVertex3d vertex) && vertex != null)
                {
                    if (type == Poly3dType.SimplePoly && vertex.VertexType == Vertex3dType.SimpleVertex)
                        result.Add(vertex.Position);
                    else if ((type == Poly3dType.CubicSplinePoly || type == Poly3dType.QuadSplinePoly)
                    && vertex.VertexType != Vertex3dType.ControlVertex)
                        result.Add(vertex.Position);
                }
            } else if (curve is Polyline polyLw) {
                for (int i = 0; i < polyLw.NumberOfVertices; i++)
                    result.Add(polyLw.GetPoint3dAt(i));
            } else if (curve is Polyline2d poly2d) {
                var type = poly2d.PolyType;
                foreach (ObjectId v2dId in poly2d) if (t.Exists(v2dId)
                    && t.TryGet(v2dId, out Vertex2d vertex) && vertex != null)
                {
                    if (type == Poly2dType.SimplePoly && vertex.VertexType == Vertex2dType.SimpleVertex)
                        result.Add(vertex.Position);
                    // Další druhy 2d polyline neřešíme
                }
            }
            return result;
        }

        List<Point2d> _Profiler_CollectVertice(Point3dCollection vertice, Curve curve)
        {
            List<Point2d> result = new List<Point2d>() { new Point2d(0.0, curve.StartPoint.Z) };
            double previous = 0.0;
            for (int i = 1; i < vertice.Count; i++)
            {
                var p1 = vertice[i];
                try
                {
                    var p2 = curve.GetClosestPointTo(p1, false);
                    double dq = curve.GetParameterAtPoint(p2);
                    double dx = curve.GetDistanceAtParameter(dq);
                    // Když máme uzavřenou polyline, tak se může stát že dx = 0;
                    if (i == vertice.Count - 1 && curve.Closed)
                        dx = curve.GetDistanceAtParameter(curve.EndParam);
                    if (dx < previous) continue;
                    previous = dx;
                    result.Add(new Point2d(dx, p2.Z));
                } catch (Exception exception) 
                { Console.WriteLine(exception.Message); }
            }
            return result;
        }

        List<List<Point2d>> _Profiler_CollectIntersectsWith(Point3dCollection vertice, Curve curve, Entity solid)
        {
            List<List<Point2d>> result = new List<List<Point2d>>();
            List<Point2d> tmp = new List<Point2d>();
            double previous = 0.0;
            using (AcBr.Brep brep = new AcBr.Brep(solid))
            {
                for (int i = 0; i < vertice.Count; i++)
                {
                    var p1 = vertice[i];
                    try
                    {
                        var p2 = curve.GetClosestPointTo(p1, false);
                        double dq = curve.GetParameterAtPoint(p2);
                        double dx = curve.GetDistanceAtParameter(dq);
                        // Když máme uzavřenou polyline, tak se může stát že dx = 0;
                        if (i == vertice.Count - 1 && curve.Closed)
                            dx = curve.GetDistanceAtParameter(curve.EndParam);
                        if (dx < previous) continue;
                        previous = dx;
                        LineSegment3d ray = new LineSegment3d(new Point3d(p2.X, p2.Y, 0), new Point3d(p2.X, p2.Y, 1E9));
                        AcBr.Hit[] hits = brep.GetLineContainment(ray, 2);
                        if (hits != null && hits.Length != 0)
                        {
                            var z = hits.Max(h => h.Point.Z);
                            tmp.Add(new Point2d(dx, z));
                        } 
                        else 
                        {
                            if (tmp.Count >= 2)
#pragma warning disable IDE0306 // Simplify collection initialization
                                result.Add(new List<Point2d>(tmp));
#pragma warning restore IDE0306 // Simplify collection initialization
                            tmp.Clear();
                        }
                    } catch (Exception exception)
                    { Console.WriteLine(exception.Message); }
                }
                if (tmp.Count >= 2)
                    result.Add(tmp);
            }
            return result;
        }
    }
}