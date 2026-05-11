#pragma warning disable CS8600, CS8601
using System; // Keep for .NET 4.6
using System.IO; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Xml;
using System.Globalization;
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
using BcToolsC.BCad.Commands.Models;

#if !NET45
using NetTopologySuite.Geometries;
#endif

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly Regex _knRegex = new Regex(@":\s*(.*?)\s*\[", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        readonly Regex _tnRegex = new Regex(@"[?&]Id=([^&]+)", RegexOptions.Compiled);
        readonly Dictionary<string, string> Kn_TypeThemeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DXF", "KM-KU-DXF" },
            { "DGN", "KM-KU-DGN" },
            // Zbytek podporovaných souborů
            { "VFK", "KM-KU-VFK" },
            { "SHP", "KM-KU-SHP" },
            { "VKM", "KM-KU-VKM" }
        };

        readonly string Kn_RuianUri = "https://atom.cuzk.cz/get.ashx?format=json&searchTerms=Rosice%20[583782]&theme=RUIAN-S-K-U&crs=&crs=JTSK";

        // https://atom.cuzk.cz/get.ashx?format=json&searchTerms={0}%20[{1}]&theme=RUIAN-S-K-U&crs=&crs=JTSK

        // Druh pozemku
        readonly Dictionary<string, string> Kn_LandTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/Hopgarden", "chmelnice" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/Vineyard", "vinice" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/Garden", "zahrada" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/Orchard", "ovocný sad" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/Grassland", "trvalý travní porost" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/Forest", "lesní půda bez rozlišení porostu" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/Park", "park, okrasná zahrada" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/InfertileLand", "neplodná půda" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/RuinsYard", "zbořeniště, společný dvůr" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/SurfaceMining", "povrchová těžba nerostů a surovin" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/Watercourse", "vodní tok širší než 2 m" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/WaterArea", "vodní nádrž, rybník" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/Swamp", "močál, bažina" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeSymbolValue/Graveyard", "hřbitov" },
            // https://services.cuzk.gov.cz/registry/codelist/LandTypeValue
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeValue/ArableGround", "orná půda" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeValue/Hopgarden", "chmelnice" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeValue/Vineyard", "vinice" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeValue/Garden", "zahrada" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeValue/Orchard", "ovocný sad" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeValue/Grassland", "trvalý travní porost" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeValue/Forest", "lesní pozemek" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeValue/WaterArea", "vodní plocha" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeValue/BuiltUpArea", "zastavěná plocha a nádvoří" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandTypeValue/OtherArea", "ostatní plocha" }
        };

        // Způsob využití
        readonly Dictionary<string, string> Kn_LandUsesMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // https://services.cuzk.gov.cz/registry/codelist/LandUseValue
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/Greenhouse", "skleník, pařeniště" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/ForestTreeNursery", "školka" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/TreePlantation", "plantáž dřevin" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/NonProductionForest", "les jiný než hospodářský" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/ForestedAreaWithBuilding", "lesní pozemek, na kterém je budova" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/Pond", "rybník" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/NaturalWatercourse", "koryto vodního toku přirozené nebo upravené" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/ArtificialWatercourse", "koryto vodního toku umělé" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/NaturalWaterTank", "vodní nádrž přírodní" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/ArtificialWaterTank", "vodní nádrž umělá" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/WaterloggedArea", "zamokřená plocha" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/SharedYard", "společný dvůr" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/Ruins", "zbořeniště" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/Railway", "dráha" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/Highway", "dálnice" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/Road", "silnice" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/OtherRoads", "ostatní komunikace" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/OtherTrafficAreas", "ostatní dopravní plocha" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/Greenery", "zeleň" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/RecreationArea", "sportoviště a rekreační plocha" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/Cemetery", "hřbitov, urnový háj" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/CulturalArea", "kulturní a osvětová plocha" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/HandlingArea", "manipulační plocha" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/MiningArea", "dobývací prostor" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/Dump", "skládka" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/OtherArea", "jiná plocha" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/InfertileLand", "neplodná půda" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/WaterSurfaceWithBuilding", "vodní plocha, na které je budova" },
            { "https://services.cuzk.gov.cz/registry/codelist/LandUseValue/PhotovoltaicPowerPlant", "fotovoltaická elektrárna" }
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
            if (!TryFetchAtomic(editor, "ZABAGED-vyskopis-DGN", wgs84, out AtomicEntries response))
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
            if (!TryFetchAtomic(editor, "CPX", wgs84, out AtomicEntries response))
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
            var parcels = ListParcel(data);
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
            if (!TryFetchAtomic(editor, "KM-KU-DXF", wgs84, out AtomicEntries response))
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
            if (!TryFetchAtomic(editor, theme, wgs84, out AtomicEntries response))
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
            if (!TryFetchAtomicWithExtents(editor, "CPX", a, b, out AtomicEntries response))
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
            var parcels = ListParcel(data);
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
                    var n = p.Geometry.Count;
                    if (n < 3) continue;
                    var tmp = new CoordinateList(p.Geometry.Count);
                    foreach (var v in p.Geometry)
                    tmp.Add(new Coordinate(v.X, v.Y));

                    // Oblast musí být uzavřena
                    Coordinate fst = tmp[0];
                    Coordinate lst = tmp[n - 1];
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
                    } catch { Console.WriteLine($"error [{p.Zuid}/{p.Puid}]:" + string.Join(" ", tmp.Select(s => $"{s.X} {s.Y}"))); continue; }
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

        private List<AcDbParcel> ListParcel(byte[] data)
        {
            var result = new List<AcDbParcel>();
            if (data == null || data.Length == 0) return result;
            try
            {
                using (var ms = new MemoryStream(data, writable: false))
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false))
                {
                    var zipEntry = archive.Entries?.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name) &&
                         e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
                    if (zipEntry == null) return result;
                    using (var stream = zipEntry.Open())
                    using (var xmlReader = XmlReader.Create(stream, new XmlReaderSettings
                    {
                        IgnoreComments = true,
                        IgnoreWhitespace = true,
                    }))
                    {
                        while (xmlReader.Read())
                        {
                            if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.LocalName == "CadastralParcel")
                            {
                                var parcel = ReadParcel(xmlReader);
                                if (parcel != null && parcel.Area != 0)
                                    result.Add(parcel);
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            { Console.WriteLine(exception.Message); }
            return result;
        }

        private AcDbParcel ReadParcel(XmlReader xmlReader)
        {
            var parcel = new AcDbParcel(xmlReader.GetAttribute("id", "http://www.opengis.net/gml/3.2"));

            const GmlGeometryContext kMask = GmlGeometryContext.referencePoint | GmlGeometryContext.Point;
            const GmlGeometryContext lMask = GmlGeometryContext.geometry
                | GmlGeometryContext.Polygon
                | GmlGeometryContext.exterior
                | GmlGeometryContext.LinearRing;
            var m = GmlGeometryContext.None;
            using (XmlReader reader = xmlReader.ReadSubtree())
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        switch (reader.LocalName)
                        {
                            case "geometry": m &= ~GmlGeometryContext.geometry; break;
                            case "Polygon": m &= ~GmlGeometryContext.Polygon; break;
                            case "exterior": m &= ~GmlGeometryContext.exterior; break;
                            case "LinearRing": m &= ~GmlGeometryContext.LinearRing; break;
                            case "referencePoint": m &= ~GmlGeometryContext.referencePoint; break;
                            case "Point": m &= ~GmlGeometryContext.Point; break;
                        }
                        continue;
                    }
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    switch (reader.LocalName)
                    {
                        // Point
                        case "referencePoint": m |= GmlGeometryContext.referencePoint; break;
                        case "Point":
                            if (m.HasFlag(GmlGeometryContext.referencePoint)) 
                                m |= GmlGeometryContext.Point;
                            break;
                        case "pos":
                            if ((m & kMask) != kMask)
                            {
                                reader.Skip();
                                continue;
                            }
                            string pos = reader.ReadElementContentAsString();
                            if (string.IsNullOrEmpty(pos)) break;
                            string[] posEntries = pos.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                            if (posEntries.Length != 2) break;
                            parcel.Point = new Point2d(
                                double.Parse(posEntries[0], CultureInfo.InvariantCulture),
                                double.Parse(posEntries[1], CultureInfo.InvariantCulture));
                            break;
                        // Geometry
                        case "geometry": m |= GmlGeometryContext.geometry; break;
                        case "Polygon":
                            if (m.HasFlag(GmlGeometryContext.geometry))
                                m |= GmlGeometryContext.Polygon;
                            break;
                        case "exterior":
                            if (m.HasFlag(GmlGeometryContext.Polygon))
                                m |= GmlGeometryContext.exterior;
                            break;
                        case "LinearRing":
                            if (m.HasFlag(GmlGeometryContext.exterior))
                                m |= GmlGeometryContext.LinearRing;
                            break;
                        case "posList":
                            if ((m & lMask) != lMask)
                            {
                                reader.Skip();
                                continue;
                            }
                            string posList = reader.ReadElementContentAsString();
                            if (string.IsNullOrEmpty(posList)) break;
                            string[] posListEntries = posList.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                            // Validace jestli má parcela dostatek bodů pro vytvoření uzavřeného polygonu
                            int n = posListEntries.Length;
                            if (n % 2 != 0)
                            {
                                Console.WriteLine($"Debug; Parcela nemá sudý počet souřadnic.");
                                continue;
                            }
                            int j = 0;
                            for (int i = 0; i + 1 < posListEntries.Length; i += 2)
                            {
                                parcel.Geometry.Add(new Point2d(
                                    double.Parse(posListEntries[i], CultureInfo.InvariantCulture),
                                    double.Parse(posListEntries[i + 1], CultureInfo.InvariantCulture)));
                                j++;
                            }
                            break;
                        // Land
                        case "landType":
                            var landType = reader.GetAttribute("xlink:href");
                            if (string.IsNullOrEmpty(landType)) parcel.Land = "ostatní plocha";
                            else
                            {
                                if (Kn_LandTypeMap.TryGetValue(landType, out string land)) parcel.Land = land;
                                else parcel.Land = "ostatní plocha";
                            }
                            break;
                        // Uses
                        case "landUse":
                            var landUse = reader.GetAttribute("xlink:href");
                            if (string.IsNullOrEmpty(landUse)) break;
                            if (Kn_LandUsesMap.TryGetValue(landUse, out string uses)) parcel.Uses = uses;
                            break;
                        // Town
                        // Tuid
                        case "administrativeUnit":
                            parcel.Town = reader.GetAttribute("xlink:title");
                            var administrativeUnit = reader.GetAttribute("xlink:href");
                            if (string.IsNullOrEmpty(administrativeUnit)) break;
                            var m0 = _tnRegex.Match(administrativeUnit);
                            if (m0.Success) parcel.Tuid = m0.Groups[1].Value;
                            break;
                        // Area
                        case "areaValue":
                            parcel.Area = reader.ReadElementContentAsDouble();
                            break;
                        // Name
                        case "label":
                            parcel.Name = reader.ReadElementContentAsString();
                            break;
                        // Zone
                        case "zoning":
                            parcel.Zone = reader.GetAttribute("xlink:title");
                            var zoning = reader.GetAttribute("xlink:href");
                            if (string.IsNullOrEmpty(zoning)) break;
                            var m1 = _tnRegex.Match(zoning);
                            if (m1.Success) parcel.Zuid = m1.Groups[1].Value;
                            break;
                        // Buid
                        case "building":
                            parcel.Buid = reader.GetAttribute("xlink:title");
                            break;
                    }
                }
            }
            return parcel;
        }

        [Flags]
        enum GmlGeometryContext 
            : short
        {
            None           = 0,         // 000000
            geometry       = 1 << 0,    // 000001, cp:geometry
            Polygon        = 1 << 1,    // 000010, gml:Polygon
            exterior       = 1 << 2,    // 000100, gml:exterior
            LinearRing     = 1 << 3,    // 001000, gml:LinearRing
            referencePoint = 1 << 4, // 010000, cp:referencePoint
            Point          = 1 << 5     // 100000, gml:Point
        }
    }
}