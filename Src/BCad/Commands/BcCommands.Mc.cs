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
using static BcToolsC.BCad.Transactions.BCadTransaction;
using NetTopologySuite.Geometries;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly AcRun.RXClass _proxyEntity = AcRun.RXObject.GetClass(typeof(ProxyEntity));
        readonly AcRun.RXClass _proxyObject = AcRun.RXObject.GetClass(typeof(ProxyObject));

        [AcRun.CommandMethod("BCTOOLSC_MC_RM_PROXY")]
        public void Mc_ClearProxy()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
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
            if (!BcApp.IsAppProperlyInitialized) return;
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
            var __curve = GetEntityFromPrompt(editor, "Vyberte křivku",
            typeof(Line), typeof(Spline), typeof(Polyline3d), typeof(Polyline2d), typeof(Polyline));
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
                if (curve is Polyline2d) editor.Warn("Křivka je staršího typu Polyline2d; Výsledek nemusí být správný");
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
                    new Point2d(dx + (2.5 * scale.sY), dy + (2.5 * scale.sY)),
                    new Point2d(dx - (2.5 * scale.sY), dy + (2.5 * scale.sY))
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
                new Point2d(dx + (1.0 * scale.sY), dy + (2.5 - .2) * scale.sY),
                height: 2.5 * scale.sY,
                vMode: AttachmentPoint.BottomLeft, additional: (m) => {
                    m.BackgroundFill = true;
                    m.BackgroundScaleFactor = 1.0;
                }));
                t.AddLWPolyline(new Point2d(dx, dy), new Point2d(dx + dl * scale.sX, dy), color: 0);

                // Vykreslení křivky
                t.AddLWPolyline(pts.Select(p => new Point2d(p.X * scale.sX + dx, (p.Y - my) * scale.sY + dy)), color: 3);
                foreach (var jmp in ter) if (jmp.Count != 0)
                    t.AddLWPolyline(jmp.Select(p => new Point2d(p.X * scale.sX + dx, (p.Y - my) * scale.sY + dy)), color: 8);
                // Vykreslení nejvyššího místa
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
            editor.Warn("Výběr byl zrušen uživatelem.");
            return;
        }

        List<Point2d> Profiler_CollectVertice(Point3dCollection vertice, Curve curve)
        {
            List<Point2d> result = new List<Point2d>();
            if (vertice.Count == 0) return result;
            result.Add(new Point2d(0.0, curve.StartPoint.Z));
            if (curve is Line line)
            {
                result.Add(new Point2d(line.Length, curve.EndPoint.Z));
                return result;
            }
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
                }
                catch (Exception exception)
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
                    }
                    catch (Exception exception)
                    { Console.WriteLine(exception.Message); }
                }
                // Doplnění posledního segmentu do výsledku
                if (tmp.Count >= 2)
                    result.Add(tmp);
            }
            return result;
        }

        class Point3dXYComparer : IEqualityComparer<Point3d>
        {
            readonly double _tolerance;
            public Point3dXYComparer(double tolerance = 1E-5) { _tolerance = tolerance; }
            public bool Equals(Point3d a, Point3d b)
                => Math.Abs(a.X - b.X) < _tolerance
                && Math.Abs(a.Y - b.Y) < _tolerance;
            private long Q(double value) => (long)Math.Round(value / _tolerance);
            public int GetHashCode(Point3d p) => (Q(p.X).GetHashCode() * 397) ^ Q(p.Y).GetHashCode();
        }

        [AcRun.CommandMethod("BCTOOLSC_MC_PROFILE_3DFACE")]
        public void Mc_Profile3dFace()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
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
            var __curve = GetEntityFromPrompt(editor, "Vyberte křivku",
            typeof(Line), typeof(Spline), typeof(Polyline3d), typeof(Polyline2d), typeof(Polyline));
            if (__curve == ObjectId.Null) goto no_data;

            var __point = GetPointFromPrompt(editor, "Vyberte vkládací bod");
            if (__point == null) goto user_closed_dialog;
            var point = __point.Value.TransformBy(ucs);
            var scale = GetScaleFromPrompt(editor, "Zadejte měřítko Y", previousScaleY)
                ?? new SCALE(1_000, 1_000);
            previousScaleY = (int)scale.Y; 

            PromptSelectionOptions options = new PromptSelectionOptions { MessageForAdding = $"\nVyberte všechny 3DFace: " };
            PromptSelectionResult evResult = editor.GetSelection(options, new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DFACE") }));
            if (evResult.Status != PromptStatus.OK) goto user_closed_dialog;
            var __faces = evResult.Value;
            if (__faces.Count == 0) goto no_data;

            Call(t =>
            {
                if (!t.TryGet(__curve, out Curve curve)) goto local_no_data;
                if (curve is Polyline2d) editor.Warn("Křivka je staršího typu Polyline2d; Výsledek nemusí být správný");
                Point3dCollection vertice = GetPolylineVertices(t, curve);
                if (vertice.Count < 2) goto local_no_data;
                if (curve.Closed) vertice.Add(vertice[0]);
                var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
                CoordinateZ[] coord = new CoordinateZ[vertice.Count];
                for (int i = 0; i < vertice.Count; i++)
                {
                    var v = vertice[i];
                    coord[i] = new CoordinateZ(v.X, v.Y, v.Z);
                }
                var polyline = factory.CreateLineString(coord);
                long n = __faces.Count / 100L;
                if (n == 0) n = 1;
                // Budování koridoru sítě
                List<Point3d> result = new List<Point3d>();
                using (AcRun.ProgressMeter progress = new AcRun.ProgressMeter())
                {
                    progress.SetLimit(100);
                    progress.Start("Buduji síť ...");
                    int l = 0;
                    for (int i = 0; i < __faces.Count; i++)
                    {
                        var f = __faces[i];
                        if (i % n == 0 && l < 100)
                        {
                            progress.MeterProgress();
                            l++;
                        }
                        try
                        {
                            if (!t.TryGet(f.ObjectId, out Face face)) continue;
                            var arr = new CoordinateZ[4];
                            for (short j = 0; j < 3; j++)
                            {
                                var m = face.GetVertexAt(j);
                                arr[j + 1] = new CoordinateZ(m.X, m.Y, m.Z);
                            }
                            var tmp = face.GetVertexAt(3);
                            arr[0] = new CoordinateZ(tmp.X, tmp.Y, tmp.Z);
                            // X == other.X && Y == other.Y
                            if (!arr[0].Equals2D(arr[3])) continue;
                            var triangle = factory.CreatePolygon(arr);
                            if (triangle.Intersects(polyline))
                            {
                                Geometry intersection = triangle.Intersection(polyline);
                                var jmp = Profiler_CollectIntersectsWith(intersection);
                                if (jmp.Count == 0) continue;
                                result.AddRange(jmp);
                            }
                        }
                        catch (Exception exception)
                        { Console.WriteLine(exception.Message); }
                    }
                    progress.Stop();
                }
                var compare = new Point3dXYComparer();
                var deduped = result.Distinct(compare).ToList();
                List<Point2d> pts = new List<Point2d>();
                n = deduped.Count / 100L;
                if (n == 0) n = 1;
                using (AcRun.ProgressMeter progress = new AcRun.ProgressMeter())
                {
                    progress.SetLimit(100);
                    progress.Start("Počítám profil ...");
                    int l = 0;
                    for (int i = 0; i < deduped.Count; i++)
                    {
                        var p1 = deduped[i];
                        if (i % n == 0 && l < 100)
                        {
                            progress.MeterProgress();
                            l++;
                        }
                        try
                        {
                            // Použití vnitřního API pro získání FitPointu
                            var p2 = curve.GetClosestPointTo(p1, false);
                            double dq = curve.GetParameterAtPoint(p2);
                            double dv = curve.GetDistanceAtParameter(dq);
                            pts.Add(new Point2d(dv, p1.Z));
                        }
                        catch (Exception exception)
                        { Console.WriteLine(exception.Message); }
                    }
                    progress.Stop();
                }
                if (pts.Count < 2) goto local_no_data;
                var ordered = pts.OrderBy(p => p.X).ToList();
                List<MText> _mTextBringFront = new List<MText>();
                double dx = point.X;
                double dy = point.Y;
                // Srovnávací rovina
                double dl;
                try { dl = curve.GetDistanceAtParameter(curve.EndParam); }
                catch (Exception) { editor.Error("Chyba; Nepovedlo se získat délku objektu."); return; }
                t.AddLWPolyline(new[] {
                    new Point2d(dx, dy),
                    new Point2d(dx + (2.5 * scale.sY), dy + (2.5 * scale.sY)),
                    new Point2d(dx - (2.5 * scale.sY), dy + (2.5 * scale.sY))
                }, shouldBeClosed: true);
                // Výška
                // Zde chceme získat výšku pro srovnávací rovinu,
                // o kterou pak opravíme souřadnici Y
                // --- VÝPOČET SROVNÁVACÍ ROVINY ---
                double minY = pts.Min(p => p.Y);
                var pMax = pts.Aggregate((a, b) => a.Y > b.Y ? a : b);
                // Zaokrouhlení dolů na nejbližší desítku
                double my = Math.Floor(minY / 10.0) * 10.0;
                _mTextBringFront.Add(t.AddMText($"{my:N3}",
                new Point2d(dx + (1.0 * scale.sY), dy + (2.5 - .2) * scale.sY),
                height: 2.5 * scale.sY,
                vMode: AttachmentPoint.BottomLeft, additional: (m) => {
                    m.BackgroundFill = true;
                    m.BackgroundScaleFactor = 1.0;
                }));
                t.AddLWPolyline(new Point2d(dx, dy), new Point2d(dx + dl * scale.sX, dy), color: 0);
                t.AddLWPolyline(ordered.Select(p => new Point2d(p.X * scale.sX + dx, (p.Y - my) * scale.sY + dy)), color: 3);
                // Vykreslení nejvyššího místa
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
            editor.Warn("Výběr byl zrušen uživatelem.");
            return;
        }

        [AcRun.CommandMethod("BCTOOLSC_MC_PROFILE_DT4")]
        public void Mc_ProfileDt4()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
        }

        private static List<Point3d> Profiler_CollectIntersectsWith(Geometry intersection,
            int recursiveDepth = 0)
        {
            List<Point3d> result = new List<Point3d>();
            if (recursiveDepth >= 3 || intersection.IsEmpty) return result;
            switch (intersection)
            {
                case Point i:
                    var i0 = i.Coordinate;
                    if (i0 == null) return result;
                    result.Add(new Point3d(i0.X, i0.Y, i0.Z));
                    break;
                case LineString i:
                    var i21 = i.StartPoint?.Coordinate;
                    var i22 = i.EndPoint?.Coordinate;
                    if (i21 == null || i22 == null) return result;
                    result.Add(new Point3d(i21.X, i21.Y, i21.Z));
                    result.Add(new Point3d(i22.X, i22.Y, i22.Z));
                    break;
                // Další druhy geometrie pro náš účel není třeba řešit
                default:
                    break;
            }
            return result;
        }
    }
}