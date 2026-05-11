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
using BcToolsC.BCad.Transactions;

#if !NET45
using NetTopologySuite.Geometries;
#endif

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        [AcRun.CommandMethod("BCTOOLSC_PR_PROFILE_SOLID")]
        public void Pr_ProfileWithSolid() => BuildProfiler(true);
        [AcRun.CommandMethod("BCTOOLSC_PR_PROFILE")]
        public void Pr_Profile() => BuildProfiler();

        int previousScaleY = 1_000;
        void BuildProfiler(bool promptWithSolid = false)
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;
            var __curve = GetEntityFromPrompt(editor, "Vyberte křivku",
            typeof(Line), typeof(Polyline), typeof(Polyline2d), typeof(Arc), typeof(Circle), typeof(Spline),
            typeof(Polyline3d));
            if (__curve == ObjectId.Null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            // Získání vstupu od uživatele
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            var point = __point.Value.TransformBy(editor.CurrentUserCoordinateSystem);
            var scale = GetScaleFromPrompt(editor, "Zadejte měřítko Y", previousScaleY)
                ?? new SCALE(1_000, 1_000);
            previousScaleY = (int)scale.Y;
            ObjectId __solid = ObjectId.Null;
            if (promptWithSolid)
            {
                __solid = GetEntityFromPrompt(editor, "Vyberte 3D těleso (Solid3d)", 
                typeof(Solid3d));
            }
            Call(t =>
            {
                if (!t.TryGet(__curve, out Curve curve))
                {
                    editor.Warn("Nebyla nalazena žádná data.");
                    return;
                }
                // Srovnávací rovina
                double dl;
                try { dl = curve.GetDistAtPoint(curve.EndPoint); }
                catch { editor.Error("Chyba; Nepovedlo se získat délku objektu."); return; }
                if (curve is Polyline2d) editor.Warn("Křivka je staršího typu Polyline2d; Výsledek nemusí být správný");
                var vertice = GetPolylineVertices(t, curve).ToList();
                int n = vertice.Count;
                if (n < 2)
                {
                    editor.Warn("Nebyla nalazena žádná data.");
                    return;
                }
                Point3d fst = vertice[0];
                Point3d lst = vertice[n - 1];
                bool reallyClosing = fst.IsEqualTo(lst, Tolerance.Global);
                if (curve.Closed && !reallyClosing) vertice.Add(fst);
                var pts = CollectVertice(vertice, curve);
                var ter = new List<List<Point2d>>();
                if (t.Exists(__solid) && t.TryGet(__solid, out Solid3d solid))
                    ter = CollectVerticeFromSolid(vertice, curve, solid).ToList();
                double minY = pts.Select(p => p.Y).Concat(ter.SelectMany(list => list).Select(p => p.Y))
                    .DefaultIfEmpty(0.0)
                    .Min();
                var maxY = pts.OrderByDescending(p => p.Y).FirstOrDefault();
                DrawProfile(t, editor, pts, ter, minY, maxY, point.X, point.Y, dl, scale);
            });
        }

        [AcRun.CommandMethod("BCTOOLSC_PR_PROFILE_3DFACE")]
        public void Pr_Profile3dFace()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateAppVersion(editor)) return;
#if !NET45
            if (!ValidateModelSpace(editor, db)) return;
            var __curve = GetEntityFromPrompt(editor, "Vyberte křivku",
            typeof(Line), typeof(Polyline), typeof(Polyline2d), typeof(Arc), typeof(Circle), typeof(Spline),
            typeof(Polyline3d));
            if (__curve == ObjectId.Null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            // Získání vstupu od uživatele
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            var point = __point.Value.TransformBy(editor.CurrentUserCoordinateSystem);
            var scale = GetScaleFromPrompt(editor, "Zadejte měřítko Y", previousScaleY)
                ?? new SCALE(1_000, 1_000);
            previousScaleY = (int)scale.Y;

            PromptSelectionOptions options = new PromptSelectionOptions { MessageForAdding = $"\nVyberte všechny 3DFace: " };
            PromptSelectionResult evResult = editor.GetSelection(options, new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DFACE") }));
            if (evResult.Status != PromptStatus.OK)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            var __faces = evResult.Value;
            if (__faces.Count == 0)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            Call(t =>
            {
                if (!t.TryGet(__curve, out Curve curve))
                {
                    editor.Warn("Nebyla nalazena žádná data.");
                    return;
                }
                // Srovnávací rovina
                double dl;
                try { dl = curve.GetDistAtPoint(curve.EndPoint); }
                catch { editor.Error("Chyba; Nepovedlo se získat délku objektu."); return; }
                if (curve is Polyline2d) editor.Warn("Křivka je staršího typu Polyline2d; Výsledek nemusí být správný");
                var vertice = GetPolylineVertices(t, curve).ToList();
                int n = vertice.Count;
                if (n < 2)
                {
                    editor.Warn("Nebyla nalazena žádná data.");
                    return;
                }
                Point3d fst = vertice[0];
                Point3d lst = vertice[n - 1];
                bool reallyClosing = fst.IsEqualTo(lst, Tolerance.Global);
                if (curve.Closed && !reallyClosing) vertice.Add(fst);
                var pts = CollectVertice(vertice, curve);
                var cmp = new List<Point3d>();
                
                // Vytvoření topology geometrie
                var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
                var polyline = factory.CreateLineString(vertice.Select(v => new CoordinateZ(v.X, v.Y, v.Z)).ToArray());

                var size = __faces.Count;
                using (AcRun.ProgressMeter progress = new AcRun.ProgressMeter())
                {
                    var last = 0;
                    progress.SetLimit(100);
                    progress.Start("Hledám průsečníky se sítí ...");
                    for (int i = 0; i < size; i++)
                    {
                        var f = __faces[i];
                        int curr = (int)((double)i / size * 100);
                        if (curr > last)
                        {
                            for (int j = 0; j < curr - last; j++)
                                progress.MeterProgress();
                            last = curr;
#pragma warning disable CA1416 // Validate platform compatibility
                            System.Windows.Forms.Application.DoEvents();
#pragma warning restore CA1416 // Validate platform compatibility

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
                            if (!arr[0].Equals2D(arr[3])) continue;
                            var triangle = factory.CreatePolygon(arr);
                            if (triangle.Intersects(polyline))
                            {
                                Geometry intersection = triangle.Intersection(polyline);
                                var jmp = CollectVerticeFromGeometry(intersection).ToList();
                                if (jmp.Count == 0) continue;
                                cmp.AddRange(jmp);
                            }
                        } catch { }
                    }
                    progress.Stop();
                }
                var ter = CollectVertice(cmp, curve, false).OrderBy(p => p.X).ToList();
                double minY = pts.Select(p => p.Y).Concat(ter.Select(p => p.Y))
                    .DefaultIfEmpty(0.0)
                    .Min();
                var maxY = pts.OrderByDescending(p => p.Y).FirstOrDefault();
                DrawProfile(t, editor, pts, new List<List<Point2d>> { ter }, minY, maxY, point.X, point.Y, dl, scale);
            });
#endif
        }
#if !NET45
        static List<Point3d>
        CollectVerticeFromGeometry(Geometry geometry)
        {
            var result = new List<Point3d>();
            switch (geometry)
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
                default:
                    break;
            }
            return result;
        }
#endif

        static void DrawProfile(BCadTransaction t, Editor editor,
            IEnumerable<Point2d> pts, IEnumerable<List<Point2d>> cmp, 
            double minY, Point2d maxY,
            double dx, double dy, double dl,
            SCALE scale)
        {
            double elevation = Math.Floor(minY / 10.0) * 10.0;
            // --- VYKRESLENÍ ---
            t.AddLWPolyline(new[] { new Point2d(dx, dy), new Point2d(dx + (2.5 * scale.sY), dy + (2.5 * scale.sY)), new Point2d(dx - (2.5 * scale.sY), dy + (2.5 * scale.sY)) }, shouldBeClosed: true);
            t.AddLWPolyline(new Point2d(dx, dy), new Point2d(dx + dl * scale.sX, dy), color: 0);
            t.AddLWPolyline(new Point2d(maxY.X * scale.sX + dx, dy), new Point2d(maxY.X * scale.sX + dx, (maxY.Y - elevation) * scale.sY + dy), color: 0);
            t.AddLWPolyline(pts.Select(p => new Point2d(p.X * scale.sX + dx, (p.Y - elevation) * scale.sY + dy)), color: 3);
            foreach (var ter in cmp)
                t.AddLWPolyline(ter.Select(p => new Point2d(p.X * scale.sX + dx, (p.Y - elevation) * scale.sY + dy)), color: 8);
            t.MoveToTop(t.AddMText($"{elevation:N3}", new Point2d(dx + (1.0 * scale.sY), dy + (2.5 - .2) * scale.sY),
                height: 2.5 * scale.sY,
                vMode: AttachmentPoint.BottomLeft, additional: (m) => { m.BackgroundFill = true; m.BackgroundScaleFactor = 1.0; }));
            editor.Ok("Ok; Vykresleno v měřítku Y 1:" + scale.Y);
        }

        static IEnumerable<Point2d>
        CollectVertice(List<Point3d> vertice, Curve curve,
            bool overrideZ = true)
        {
            if (vertice == null || vertice.Count == 0) yield break;
            yield return new Point2d(0.0, curve.StartPoint.Z);
            if (curve is Line line)
            {
                yield return new Point2d(line.Length, curve.EndPoint.Z);
                yield break;
            }
            double dp = -1.0;
            double dl = curve.GetDistAtPoint(curve.EndPoint);
            for (int i = 1; i < vertice.Count; i++)
            {
                var p1 = vertice[i];
                double dx;
                double dy = p1.Z;
                try
                {
                    var p2 = curve.GetClosestPointTo(p1, false);
                    dx = curve.GetDistAtPoint(p2);
                    if (overrideZ)
                        dy = p2.Z;
                    if (i == vertice.Count - 1 && curve.Closed && dx < (dl * 0.1))
                        dx = dl;
                }
                catch { continue; }
                if (dx <= dp + 1E-5 && i != 0)
                    continue;
                dp = dx;
                yield return new Point2d(dx, dy);
            }
        }

        static IEnumerable<List<Point2d>>
        CollectVerticeFromSolid(List<Point3d> vertice, Curve curve, Solid3d solid)
        {
            List<Point2d> segment = new List<Point2d>();
            double dp = -1.0;
            double dl = curve.GetDistAtPoint(curve.EndPoint);
            using (AcBr.Brep brep = new AcBr.Brep(solid))
            {
                for (int i = 0; i < vertice.Count; i++)
                {
                    var p1 = vertice[i];
                    double dx;
                    double? dy = null;
                    var gap = false;
                    try
                    {
                        var p2 = curve.GetClosestPointTo(p1, false);
                        dx = curve.GetDistAtPoint(p2);
                        if (i == vertice.Count - 1 && curve.Closed && dx < (dl * 0.1))
                            dx = dl;
                        if (dx <= dp + 1E-5 && i != 0)
                            continue;
                        dp = dx;

                        // Vytvoření řezacího paprsku
                        using (LineSegment3d ray = new LineSegment3d(new Point3d(p2.X, p2.Y, -1E5), new Point3d(p2.X, p2.Y, 1E5)))
                        {
                            AcBr.Hit[] hits = brep.GetLineContainment(ray, 9);
                            if (hits != null && hits.Length > 0)
                            {
                                dy = hits.Max(h => h.Point.Z);
                                foreach (var hit in hits) hit.Dispose();
                            }
                            else
                            {
                                if (segment.Count >= 2)
                                    gap = true;
                            }
                        }
                    }
                    catch { continue; }
                    if (gap)
                    {
                        yield return new List<Point2d>(segment);
                        segment.Clear();
                    }
                    if (dy.HasValue)
                        segment.Add(new Point2d(dx, dy.Value));
                }
                if (segment.Count >= 2)
                    yield return new List<Point2d>(segment);
            }
        }
    }
}