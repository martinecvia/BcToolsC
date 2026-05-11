#pragma warning disable CS8600, CS8601
using System; // Keep for .NET 4.6
using System.IO; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Text.RegularExpressions;
using System.Windows;

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
#endif
#endregion

using BcToolsC.Models;
using static BcToolsC.BCad.Transactions.BCadTransaction;

#if !NET45
using NetTopologySuite.Geometries;
#endif

namespace BcToolsC.BCad.Commands
{
    // https://cuzk.gov.cz/Katastr-nemovitosti/Poskytovani-udaju-z-KN/Ciselniky-ISKN/Ciselniky-k-nemovitosti.aspx
    public partial class BcCommands
    {
        static Regex _knRegex = new Regex(@":\s*(.*?)\s*\[", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static Dictionary<string, string> Kn_TypeThemeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DXF", "KM-KU-DXF" },
            { "DGN", "KM-KU-DGN" },
            // Zbytek podporovaných souborů
            { "VFK", "KM-KU-VFK" },
            { "SHP", "KM-KU-SHP" },
            { "VKM", "KM-KU-VKM" }
        };

        [AcRun.CommandMethod("BCTOOLSC_KN_VR")]
        public void Kn_DownloadAndAttachVrstevnice()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;
            if (!ValidateDrawingPath(editor, out string dir)) return;
            if (!ValidateDirectoryWritable(editor, dir)) return;

            // Získání vstupu od uživatele
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            if (!ValidatePointInsideRelief(editor, __point.Value, out Point3d point)) return;
            var wgs84 = GetWGS84FromPoint(point);

            // Stažení dat ze serveru ČÚZK
            if (!TryFetchAtomic(editor, out AtomicEntries response, "ZABAGED-vyskopis-DGN", wgs84))
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }

            // Výběr konkrétního mapového listu
            if (!TrySelectEntry(editor, response, "mapový list: ", out AtomicEntries.Entry entry))
            return;

            // Stažení dat
            byte[] data = DownloadDataWithProgress(entry.Link);
            if (data == null || data.Length == 0)
            {
                editor.Error("Chyba; Nepovedlo se stáhnout data ve stanoveném čase.");
                return;
            }

            // Rozbalení a vložení do výkresu
            if (!TryUnzipData(data, dir, ".dgn", out string anyFile))
            {
                editor.Error("Chyba; Nepovedlo se uložit soubor.");
                return;
            }

            // Změna cest, soubory můžou mít jiné jméno souborů než je název archivu
            string dgnFile = Path.ChangeExtension(anyFile, ".dgn");
            document.SendStringToExecute("_.-DGNATTACH\n" +
                         $"\"{dgnFile}\"\n" +
                          "\n" +
                          "_Master\n" +
                          "*0,0,0\n" +
                          // ^ * stanovuje WCS souřadnice
                          "1\n" +
                          "0\n", true, false, false);
        }

        [AcRun.CommandMethod("BCTOOLSC_KN_IN")]
        public void Kn_InfoAboutParcel()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;

            // Získání vstupu od uživatele
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            if (!ValidatePointInsideRelief(editor, __point.Value, out Point3d point)) return;
            var wgs84 = GetWGS84FromPoint(point);

            // Stažení dat ze serveru ČÚZK
            if (!TryFetchAtomic(editor, out AtomicEntries response, "CPX", wgs84))
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }

            // Výběr konkrétního mapového listu
            if (!TrySelectEntry(editor, response, _knRegex, out AtomicEntries.Entry entry))
                return;

            // Stažení dat
            byte[] data = DownloadDataWithProgress(entry.Link);
            if (data == null || data.Length == 0)
            {
                editor.Error("Chyba; Nepovedlo se stáhnout data ve stanoveném čase.");
                return;
            }
            var parcels = AcDbParcel.ListParcelZip(data);
            // Kontrola jestli jsme vevnitř
            AcDbParcel parcel = null;
            for (int i = 0; i < parcels.Count; i++)
            {
                var p = parcels[i];
                if (IsPointInPolygon(point, p.Geometry))
                {
                    parcel = p;
                    break;
                }
            }
            if (parcel == null)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            Call(t => t.AddLWPolyline(t.ConvertToPoint(parcel.Geometry), color: 1));
            editor.Info($"https://vdp.cuzk.gov.cz/vdp/ruian/parcely/{parcel.Puid.Substring(4)}");
            MessageBox.Show(
                $"Obec: {parcel.Town} {parcel.Tuid}\n" +
                $"Katastrální území: {parcel.Zone} {parcel.Zuid}\n" +
                $"Kmenové číslo / poddělení: {parcel.Name}\n" +
                $"Výměra parcely [m2]: {parcel.Area}\n" +
                $"Druh pozemku: {parcel.Land}\n" +
                $"Způsob využití: {parcel.Uses}\n",
                $"Parcela: {parcel.Puid}",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        [AcRun.CommandMethod("BCTOOLSC_KN_AT")]
        public void Kn_DownloadAndAttachKn()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;
            if (!ValidateDrawingPath(editor, out string dir)) return;
            if (!ValidateDirectoryWritable(editor, dir)) return;

            // Získání vstupu od uživatele
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            if (!ValidatePointInsideRelief(editor, __point.Value, out Point3d point)) return;
            var wgs84 = GetWGS84FromPoint(point);

            // Stažení dat ze serveru ČÚZK
            if (!TryFetchAtomic(editor, out AtomicEntries response, "KM-KU-DXF", wgs84))
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }

            // Výběr konkrétního mapového listu
            if (!TrySelectEntry(editor, response, _knRegex, out AtomicEntries.Entry entry))
                return;

            // Stažení dat
            byte[] data = DownloadDataWithProgress(entry.Link);
            if (data == null || data.Length == 0)
            {
                editor.Error("Chyba; Nepovedlo se stáhnout data ve stanoveném čase.");
                return;
            }

            // Rozbalení a vložení do výkresu
            if (!TryUnzipData(data, dir, ".dxf", out string anyFile))
            {
                editor.Error("Chyba; Nepovedlo se uložit soubor.");
                return;
            }

            // Změna cest, soubory můžou mít jiné jméno souborů než je název archivu
            string dxfFile = Path.ChangeExtension(anyFile, ".dxf");
            string logFile = Path.ChangeExtension(anyFile, ".log");
            Call(t =>
            {
                using (var tb = new Database(false, true))
                {
                    tb.DxfIn(dxfFile, logFile);
                    db.Insert(Matrix3d.Identity, tb, false);
                }
            });
        }

        [AcRun.CommandMethod("BCTOOLSC_KN_DW")]
        public void Kn_DownloadKn()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            Database db = document.Database;
            Editor editor = document.Editor;
            if (!ValidateModelSpace(editor, db)) return;
            if (!ValidateDrawingPath(editor, out string dir)) return;
            if (!ValidateDirectoryWritable(editor, dir)) return;

            // Získání vstupu od uživatele
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            if (!ValidatePointInsideRelief(editor, __point.Value, out Point3d point)) return;
            var __theme = GetKeywordFromPrompt(editor, "Vyberte formát", Kn_TypeThemeMap.Keys.ToArray());
            if (string.IsNullOrEmpty(__theme) || !Kn_TypeThemeMap.TryGetValue(__theme, out string theme))
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            var wgs84 = GetWGS84FromPoint(point);

            // Stažení dat ze serveru ČÚZK
            if (!TryFetchAtomic(editor, out AtomicEntries response, theme, wgs84))
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }

            // Výběr konkrétního mapového listu
            if (!TrySelectEntry(editor, response, _knRegex, out AtomicEntries.Entry entry))
                return;

            // Výběr místa uložení
            CommonOpenFileDialog dialog = new CommonOpenFileDialog
            {
                Title = "Vyber místo pro uložení souborů",
                Multiselect = false,
                ForceFileSystem = true,
                InputPath = dir,
            };
            if (dialog.ShowDialog() != true)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }

            // Stažení dat
            byte[] data = DownloadDataWithProgress(entry.Link);
            if (data == null || data.Length == 0)
            {
                editor.Error("Chyba; Nepovedlo se stáhnout data ve stanoveném čase.");
                return;
            }

            // Rozbalení a vložení do výkresu
            if (!TryUnzipData(data, dialog.ResultPath, 
                // Není potřeba
                string.Empty, out string _))
            {
                editor.Error("Chyba; Nepovedlo se uložit soubor.");
                return;
            }

            editor.Ok("Ok; Soubory uloženy do: " + dialog.ResultPath);
        }

        [AcRun.CommandMethod("BCTOOLSC_KN_AN")]
        public void Kn_ExportParcel()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            Database db = document.Database;
            Editor editor = document.Editor;
            if (!ValidateAppVersion(editor)) return;
#if !NET45
            if (!ValidateModelSpace(editor, db)) return;
            // Získání vstupu od uživatele
            var __curve = GetEntityFromPrompt(editor, "Vyberte křivku",
            typeof(Polyline), typeof(Polyline2d), typeof(Polyline3d));
            if (__curve == ObjectId.Null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            // Vytvoření topology geometrie
            var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            var polygon = Wrap(t =>
            {
                if (!t.TryGet(__curve, out Curve curve))
                {
                    editor.Warn("Nebyla nalazena žádná data.");
                    return null;
                }
                if (curve is Polyline2d) editor.Warn("Křivka je staršího typu Polyline2d; Výsledek nemusí být správný");
                var vertice = GetPolylineVertices(t, curve).ToList();
                int n = vertice.Count;
                if (n < 3)
                {
                    editor.Warn("Nebyla nalazena žádná data.");
                    return null;
                }
                Point3d fst = vertice[0];
                Point3d lst = vertice[n - 1];
                if (!curve.Closed && !fst.IsEqualTo(lst, Tolerance.Global))
                {
                    editor.Warn("Vybraná křivka musí být uzavřená.");
                    return null;
                }
                var tmp = new CoordinateList(n);
                foreach (var v in vertice)
                tmp.Add(new Coordinate(v.X, v.Y));
                if (!tmp[0].Equals2D(tmp[n - 1]))
                tmp.Add(new Coordinate(tmp[0].X, tmp[0].Y));
                if (tmp.Count < 4)
                {
                    editor.Warn("Vybranou křivku nelze převést na referenci polygonu.");
                    return null;
                }
                Polygon result;
                try
                {
                    var x9 = factory.CreateLinearRing(tmp.ToArray());
                    result = factory.CreatePolygon(x9);
                } catch { editor.Warn("Vybranou křivku nelze převést na referenci polygonu."); return null; }
                return result;
            });

            if (polygon is null || !polygon.IsValid)
                return;

            var bbox = polygon.EnvelopeInternal;
            if (bbox.Height > 5000 || bbox.Width > 5000)
            {
                editor.Warn("Oblast přesahuje povolený limit [e=5000]");
                return;
            }

            var a = GetWGS84FromPoint(new Point3d(bbox.MinX, bbox.MinY, 0));
            var b = GetWGS84FromPoint(new Point3d(bbox.MaxX, bbox.MaxY, 0));

            // Stažení dat ze serveru ČÚZK
            if (!TryFetchAtomic(editor, out AtomicEntries response, "CPX", a, b))
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }

            // Výběr konkrétního mapového listu
            if (!TrySelectEntry(editor, response, _knRegex, out AtomicEntries.Entry entry))
                return;

            // Stažení dat
            byte[] data = DownloadDataWithProgress(entry.Link);
            if (data == null || data.Length == 0)
            {
                editor.Error("Chyba; Nepovedlo se stáhnout data ve stanoveném čase.");
                return;
            }
            var parcels = AcDbParcel.ListParcelZip(data);
            var size = parcels.Count;
            if (size == 0)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            var scope = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(polygon);
            var intersected = new List<IntersectedParcel>();
            using (AcRun.ProgressMeter progress = new AcRun.ProgressMeter())
            {
                var last = 0;
                progress.SetLimit(100);
                progress.Start("Hledám průsečníky s katastrem ...");
                for (int i = 0; i < size; i++)
                {
                    var p = parcels[i];
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
                    int rows = p.Geometry.GetLength(0);
                    if (rows < 3) continue;
                    int cols = p.Geometry.GetLength(1);
                    if (cols < 2) continue;
                    var tmp = new CoordinateList(rows);
                    for (int j = 0; j < rows; j++)
                        tmp.Add(new Coordinate(p.Geometry[i, 0], p.Geometry[i, 1]));

                    // Oblast musí být uzavřena
                    Coordinate fst = tmp[0];
                    Coordinate lst = tmp[rows - 1];
                    if (!fst.Equals2D(lst))
                    tmp.Add(new Coordinate(fst.X, fst.Y));
                    if (tmp.Count < 4) continue;
                    Polygon parcel;
                    try
                    {
                        var x9 = factory.CreateLinearRing(tmp.ToArray());
                        parcel = factory.CreatePolygon(x9);
                        if (!parcel.IsValid)
                            parcel = (Polygon)parcel.Buffer(0);
                    } catch { Console.WriteLine($"Error [{p.Zuid}/{p.Puid}]:" + string.Join(" ", tmp.Select(s => $"{s.X} {s.Y}"))); continue; }
                    if (parcel is null || !parcel.IsValid
                        || !parcel.EnvelopeInternal.Intersects(polygon.EnvelopeInternal)
                        || !scope.Intersects(parcel))
                        continue;
                    var intersection = parcel.Intersection(polygon);
                    double area = intersection.Area;
                    if (area <= 0) continue;
                    intersected.Add(new IntersectedParcel(p, area));
                    Console.WriteLine($"{i};{p.Zone};{p.Name};{p.Puid};{p.Land};{p.Uses};{p.Area};{area}");
                }
                progress.Stop();
            }
#endif
        }
    }
}