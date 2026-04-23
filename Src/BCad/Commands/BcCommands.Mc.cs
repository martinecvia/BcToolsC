#pragma warning disable IDE0028, IDE0057, IDE0062, IDE0063, IDE0090, IDE1006
using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using AcBr = ZwSoft.ZwCAD.BoundaryRepresentation;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcBr = Autodesk.AutoCAD.BoundaryRepresentation;
#endif
#endregion

using BcToolsC.Models;
using BcToolsC.BCad.Transactions;
using static BcToolsC.BCad.Transactions.BCadTransaction;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly AcRun.RXClass _proxyEntity = AcRun.RXObject.GetClass(typeof(ProxyEntity));
        readonly AcRun.RXClass _proxyObject = AcRun.RXObject.GetClass(typeof(ProxyObject));

        [AcRun.CommandMethod("BCTOOLSC_MC_RM_PROXY")]
        public void Mc_ClearProxy()
        {
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;
            if (!db.TileMode)
            {
                editor.Warn("Povoleno pouze v modelovém prostoru.");
                return;
            }
            long n = db.Handseed.Value / 100L;
            if (n == 0) n = 1;
            using (AcRun.ProgressMeter progress = new AcRun.ProgressMeter())
            {
                progress.SetLimit(100);
                progress.Start("Procházím ...");
                // https://forums.autodesk.com/t5/net-forum/proxyobjects-amp-proxyentities-how-to-find-all/td-p/10867012
                var i = 0;
                Call(t =>
                {
                    try
                    {
                        var h = db.BlockTableId.Handle;
                        var l = h.Value;
                        while (true)
                        {
                            long p = l;
                            h = db.Handseed;
                            long c = h.Value;
                            if (p >= c) break;
                            if (l % n == 0L) progress.MeterProgress();
                            if (db.TryGetObjectId(new Handle(l), out ObjectId id) && !id.IsErased)
                            {
                                if (id.ObjectClass.IsDerivedFrom(_proxyEntity))
                                {
                                    // ProxyEntity
                                    if (t.TryGet(id, out ProxyEntity e, OpenMode.ForWrite) && !e.IsErased && (e.ProxyFlags & 1) == 1)
                                    {
                                        e.Erase(true);
                                        ++i;
                                    }
                                }
                                else if (id.ObjectClass.IsDerivedFrom(_proxyObject))
                                {
                                    // ProxyObject
                                    if (t.TryGet(id, out ProxyObject o, OpenMode.ForWrite) && !o.IsErased && (o.ProxyFlags & 1) == 1)
                                    {
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
                editor.Ok("Ok; Smazaných proxy objektů: " + i);
            }
        }

        [AcRun.CommandMethod("BCTOOLSC_MC_PROFILE_SOLID")]
        public void Mc_ProfileWithSolid() => BuildProfiler(true);
        [AcRun.CommandMethod("BCTOOLSC_MC_PROFILE")]
        public void Mc_ProfileEmpty() => BuildProfiler();

        int previousScaleY = 1_000;
        void BuildProfiler(bool promptWithSolid = false)
        {
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;
            if (!db.TileMode)
            {
                editor.Warn("Povoleno pouze v modelovém prostoru.");
                return;
            }
            Matrix3d ucs = editor.CurrentUserCoordinateSystem;
            var __curve = GetEntityFromPrompt(editor, "Vyberte křivku (Polyline, Polyline3d)", 
            typeof(Polyline3d), typeof(Polyline2d), typeof(Polyline));
            if (__curve == ObjectId.Null) goto no_data;

            var __point = GetPointFromPrompt(editor, "Vyberte vkládací bod");
            if (__point == null) goto user_closed_dialog;
            var point = __point.Value.TransformBy(ucs);
            var scale = GetScaleFromPrompt(editor, "Zadejte měřítko Y", previousScaleY) 
                ?? new SCALE(1_000, 1_000);
            previousScaleY = (int)scale.Y;
            ObjectId __solid = ObjectId.Null;
            if (promptWithSolid)
            {
                __solid = GetEntityFromPrompt(editor, "Vyberte 3D těleso (Solid3d)", typeof(Solid3d));
                if (__solid == ObjectId.Null) goto user_closed_dialog;
            }
            Call(t =>
            {
                if (!t.TryGet(__curve, out Curve curve)) goto local_no_data;
                Point3dCollection vertice = GetPolylineVertices(t, curve);
                if (vertice.Count < 2) goto local_no_data;
                if (curve.Closed) vertice.Add(vertice[0]);

                var pts = Profiler_CollectVertice(vertice, curve);
                if (pts.Count < 2) goto local_no_data;
                var ter = new List<List<Point2d>>();
                if (t.Exists(__solid) && t.TryGet(__solid, out Solid3d solid))
                    ter = Profiler_CollectIntersectsWith(vertice, curve, solid);
                List<MText> _mTextBringFront = new List<MText>();
                double dx = point.X;
                double dy = point.Y;
                // Srovnávací rovina
                double dl;
                try { dl = curve.GetDistanceAtParameter(curve.EndParam); }
                catch (Exception) { editor.Error("Chyba; Nepovedlo se získat délku objektu."); return; }
                t.AddLWPolyline(new[] {
                    new Point2d(dx, dy),
                    new Point2d(dx + 2.5, dy + 2.5),
                    new Point2d(dx - 2.5, dy + 2.5)
                }, shouldBeClosed: true);
                // Zde chceme získat výšku pro srovnávací rovinu,
                // o kterou pak opravíme souřadnici Y
                // --- VÝPOČET SROVNÁVACÍ ROVINY ---
                double minY = pts.Min(p => p.Y);
                var pMax = pts.Aggregate((a, b) => a.Y > b.Y ? a : b);
                if (ter.Count > 0 && ter.Any(list => list.Count > 0))
                {
                    double minSolidY = ter.SelectMany(list => list).Min(p => p.Y);
                    minY = Math.Min(minY, minSolidY);
                }
                // Zaokrouhlení dolů na nejbližší desítku
                double my = Math.Floor(minY / 10.0) * 10.0;
                _mTextBringFront.Add(t.AddMText($"{my:N3}",
                new Point2d(dx + 1.0, dy + 2.5 - .2),
                vMode: AttachmentPoint.BottomLeft, additional: (m) => {
                    m.BackgroundFill = true;
                    m.BackgroundScaleFactor = 1.0;
                }));
                t.AddLWPolyline(new Point2d(dx, dy), new Point2d(dx + dl * scale.sX, dy), color: 0);
                // Vykreslení křivky
                t.AddLWPolyline(pts.Select(p => new Point2d(p.X * scale.sX + dx, (p.Y - my) * scale.sY + dy)), color: 3);
                foreach (var jmp in ter) if (jmp.Count != 0)
                    t.AddLWPolyline(jmp.Select(p => new Point2d(p.X * scale.sX + dx, (p.Y - my) * scale.sY + dy)), color: 8);
                // Vykreslení nevjyvššího místa
                t.AddLWPolyline(new Point2d(pMax.X * scale.sX + dx, dy), new Point2d(pMax.X * scale.sX + dx, (pMax.Y - my) * scale.sY + dy), color: 0);
                // Posunutí textů nad kresbu
                foreach (var mtx in _mTextBringFront) t.MoveToTop(mtx);
                editor.Ok("Ok; Vykresleno v měřítku Y 1:" + scale.Y);
                return;
            local_no_data:
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            });
            return;
        no_data:
            editor.Warn("Nebyla nalazena žádná data.");
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

        List<Point2d> Profiler_CollectVertice(Point3dCollection vertice, Curve curve)
        {
            List<Point2d> result = new List<Point2d>();
            if (vertice.Count == 0) return result;
            result.Add(new Point2d(0.0, curve.StartPoint.Z));
            double previous = 0.0;
            for (int i = 1; i < vertice.Count; i++)
            {
                var p1 = vertice[i];
                try
                {
                    // Použití vnitřního API pro získání FitPointu
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

        List<List<Point2d>> Profiler_CollectIntersectsWith(Point3dCollection vertice, Curve curve, Solid3d solid)
        {
            List<List<Point2d>> result = new List<List<Point2d>>();
            if (vertice.Count == 0) return result;
            List<Point2d> tmp = new List<Point2d>();
            double previous = 0.0;
            using (AcBr.Brep brep = new AcBr.Brep(solid))
            {
                for (int i = 0; i < vertice.Count; i++)
                {
                    var p1 = vertice[i];
                    try
                    {
                        // Použití vnitřního API pro získání FitPointu
                        var p2 = curve.GetClosestPointTo(p1, false);
                        double dq = curve.GetParameterAtPoint(p2);
                        double dx = curve.GetDistanceAtParameter(dq);
                        // Když máme uzavřenou polyline, tak se může stát že dx = 0;
                        if (i == vertice.Count - 1 && curve.Closed)
                            dx = curve.GetDistanceAtParameter(curve.EndParam);
                        if (dx < previous) continue;
                        previous = dx;
                        LineSegment3d ray = new LineSegment3d(new Point3d(p2.X, p2.Y, 0), new Point3d(p2.X, p2.Y, 1E9));
                        AcBr.Hit[] hits = brep.GetLineContainment(ray, 9);
                        if (hits != null && hits.Length != 0)
                        {
                            var z = hits.Max(h => h.Point.Z);
                            tmp.Add(new Point2d(dx, z));
                        } 
                        else 
                        {
                            // Segment je ukončený, resp v tomhle místě už paprsek neřeže objektem
                            if (tmp.Count >= 2)
#pragma warning disable IDE0306 // Simplify collection initialization
                                result.Add(new List<Point2d>(tmp));
#pragma warning restore IDE0306 // Simplify collection initialization
                            tmp.Clear();
                        }
                    } catch (Exception exception)
                    { Console.WriteLine(exception.Message); }
                }
                // Doplnění posledního segmentu do výsledku
                if (tmp.Count >= 2)
                    result.Add(tmp);
            }
            return result;
        }
    }
}