#pragma warning disable CS8600, IDE0090
using System; // Keep for .NET 4.6
using System.IO; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Text.RegularExpressions;
using System.Windows;
using System.IO.Compression;
using System.Xml;
using System.Globalization;

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
using NetTopologySuite.Geometries;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly Regex _knRegex = new Regex(@":\s*(.*?)\s*\[",RegexOptions.Compiled | RegexOptions.IgnoreCase);
        readonly Dictionary<string, string> Kn_TypeThemeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DXF", "KM-KU-DXF" },
            { "DGN", "KM-KU-DGN" },
            // Zbytek podporovaných souborů
            { "VFK", "KM-KU-VFK" },
            { "SHP", "KM-KU-SHP" },
            { "VKM", "KM-KU-VKM" }
        };

        public sealed class AcParcel
        {
            public readonly string Id;
            public readonly string Kuid;
            public AcParcel(string id, string kuid)
            {
                Id = id;
                Kuid = kuid;
            }

            public LinearRing Geometry { get; set; }
            public Point2d Point { get; set; }
            public double Area { get; set; }
            public string Name { get; set; }
            public string Town { get; set; }
        }

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
            AtomicEntries response = null;
            try
            {
                string url = string.Format("https://atom.cuzk.cz/get.ashx?format=json&searchTerms=&theme={0}&crs=JTSK&bbox={1},{2},{1},{2}",
                    "ZABAGED-vyskopis-DGN", wgs84.L, wgs84.B);
                Console.WriteLine(url);
                editor.Info("Kontaktuji ... https://atom.cuzk.cz");
                string json = DownloadString(url);
                if (string.IsNullOrWhiteSpace(json))
                    throw new Exception("Prázdná odpověď serveru.");
                response = Deserialize<AtomicEntries>(json);
            }
            catch (Exception exception)
            { editor.Error("Chyba; " + exception.Message); return; }
            if (response?.Entries == null || response.Entries.Count == 0)
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
            if (!TryUnzipData(data, dir, out string anyFile))
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

        [AcRun.CommandMethod("BCTOOLSC_KN_AN")]
        public void Kn_ExportParcels()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            Database db = document.Database;
            Editor editor = document.Editor;
            if (!db.TileMode)
            {
                editor.Warn("Povoleno pouze v modelovém prostoru.");
                return;
            }
            var __curve = GetEntityFromPrompt(editor, "Vyberte křivku",
            typeof(Spline), typeof(Polyline3d), typeof(Polyline2d), typeof(Polyline));
            if (__curve == ObjectId.Null)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            var __point = GetPointFromPrompt(editor, "Vyberte bod v katastrálním území");
            if (__point == null)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            if (!ValidatePointInsideRelief(editor, __point.Value, out Point3d point)) return;
            var __cdata = GetCurve(__curve);
            if (__cdata == null)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            var cdata = __cdata.Value;
            int o = cdata.Vertices.Count;
            if (o < 2)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            Coordinate[] coord = new Coordinate[o];
            for (int i = 0; i < o; i++)
            {
                var v = cdata.Vertices[i];
                // Ignorujeme Z souřadnici křivky, protože ji nemáme jak porovnat
                coord[i] = new Coordinate(v.X, v.Y);
            }
            var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            if (!coord[0].Equals2D(coord[o - 1]))
            {
                editor.Warn("Nebyla nalezena uzavřená křivka pro tuto operaci.");
                return;
            }
            var polyline = factory.CreatePolygon(coord);
            var wgs84 = GetWGS84FromPoint(point);
            AtomicEntries response = null;
            try
            {
                string url = string.Format("https://atom.cuzk.cz/get.ashx?format=json&searchTerms=&theme=CP&crs=JTSK&bbox={0},{1},{0},{1}",
                    wgs84.L, wgs84.B);
                editor.Info("Kontaktuji ... https://atom.cuzk.cz");
                string json = DownloadString(url);
                if (string.IsNullOrWhiteSpace(json))
                    throw new Exception("Prázdná odpověď serveru.");
                response = Deserialize<AtomicEntries>(json);
            }
            catch (Exception exception)
            {
                editor.Error(exception.Message);
                return;
            }
            int n = response?.Entries?.Count ?? 0;
            if (response?.Entries == null || n == 0)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            // Výběr entry, pokud je entries víc jak jeden,
            // uživatel je dotázán který konkrétní objekt chce
            AtomicEntries.Entry entry;
            if (n != 1)
            {
                List<string> keywords = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    AtomicEntries.Entry k = response.Entries[i];
                    var match = _knRegex.Match(k.Name);
                    if (match.Success)
                        keywords.Add(match.Groups[1].Value.Trim());
                }
                if (keywords.Count == 0)
                {
                    editor.Warn("Nebyla nalazena žádná data.");
                    return;
                }
                var __entry = GetKeywordFromPrompt(editor, "Vyberte mapový list", keywords.ToArray());
                if (__entry == null) 
                {
                    editor.Warn("Výběr byl zrušen uživatelem.");
                    return;
                }
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                entry = response.Entries.FirstOrDefault(e => e.Name.Contains(__entry));
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
            }
            else entry = response.Entries[0];
            if (entry == null)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            var data = DownloadDataWithProgress(entry.Link);
            if (data == null || data.Length == 0)
            {
                editor.Ok("Chyba; Nepovedlo se stáhnout data ve stanoveném čase.");
                return;
            }
            var parcels = Kn_CollectParcels(data, factory);
            int q = parcels.Count;
            if (q == 0)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            long r = q / 100L;
            if (r == 0) r = 1;
            using (AcRun.ProgressMeter progress = new AcRun.ProgressMeter())
            {
                progress.SetLimit(100);
                progress.Start("Hledám průsečníky ...");
                int l = 0;
                for (int i = 0; i < q; i++)
                {
                    var p = parcels[i];
                    if (p.Geometry == null) continue;
                    if (i % n == 0 && l < 100)
                    {
                        progress.MeterProgress();
                        l++;
                    }
                    try
                    {
                        if (!GetIntersectionArea(p.Geometry, polyline, out Geometry intersection, out double area)) continue;
                        if (intersection != null && intersection.Coordinates.Length > 2)
                            Call(t => t.AddLWPolyline(intersection.Coordinates.Select(c => new Point2d(c.X, c.Y)), color: 1));
                        else
                            Call(t => t.AddLWPolyline(p.Geometry.Coordinates.Select(c => new Point2d(c.X, c.Y)), color: 5));
                    }
                    catch (Exception exception)
                    { Console.WriteLine(exception.Message); }
                }
                progress.Stop();
            }
        }

        static List<AcParcel> Kn_CollectParcels(byte[] data, GeometryFactory factory)
        {
            List<AcParcel> result = new List<AcParcel>();
            if (data == null || data.Length == 0)
                return result;
            try
            {
                using (var ms = new MemoryStream(data, writable: false))
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false))
                {
                    var zipEntry = archive.Entries?.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name) &&
                         e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
                    if (zipEntry == null) return result;
                    using (var stream = zipEntry.Open())
                    using (var reader = XmlReader.Create(stream, new XmlReaderSettings
                    {
                        IgnoreComments = true,
                        IgnoreWhitespace = true,
                    }))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element &&
                                            reader.Name == "cp:CadastralParcel")
                            {
                                AcParcel parcel = new AcParcel(
                                    reader.GetAttribute("id", "http://www.opengis.net/gml/3.2"),
                                    zipEntry.Name
                                    // Odstranění pozůstatkového .xml
                                    .Substring(0, zipEntry.Name.Length - 4)
                                );
                                using (XmlReader parcelReader = reader.ReadSubtree())
                                {
                                    while (parcelReader.Read())
                                    {
                                        if (parcelReader.NodeType != XmlNodeType.Element) continue;
                                        if (reader.LocalName == "label")
                                            parcel.Name = reader.ReadElementContentAsString();
                                        // Hodnota se chivá jako Int32. nemůžeme ale vyloučit 
                                        if (reader.LocalName == "areaValue")
                                            parcel.Area = reader.ReadElementContentAsDouble();
                                        // Není blíže specifikováno co je identifikátorem města
                                        else if (reader.LocalName == "zoning")
                                            parcel.Town = reader.GetAttribute("xlink:title");
                                        else if (reader.LocalName == "pos")
                                        {
                                            string pos = reader.ReadElementContentAsString();
                                            if (string.IsNullOrEmpty(pos)) continue;
                                            string[] posList = pos.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                                            parcel.Point = new Point2d(
                                                double.Parse(posList[0], CultureInfo.InvariantCulture),
                                                double.Parse(posList[1], CultureInfo.InvariantCulture));
                                        }
                                        else if (reader.LocalName == "posList")
                                        {
                                            string pos = reader.ReadElementContentAsString();
                                            if (string.IsNullOrEmpty(pos)) continue;
                                            // Dělení -614529.93 -1076451.27 -614532.53 -1076449.18,
                                            // na jednotlivé souřadnice
                                            string[] posList = pos.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                                            // Validace jestli má parcela dostatek bodů pro vytvoŘení uzavřeného polygonu
                                            int n = posList.Length;
                                            if (n % 2 != 0)
                                            {
                                                Console.WriteLine($"Debug; Parcela nemá sudý počet souřadnic.");
                                                continue;
                                            }
                                            Coordinate[] coord = new Coordinate[n / 2];
                                            int j = 0;
                                            for (int i = 0; i + 1 < posList.Length; i += 2)
                                            {
                                                coord[j] = new Coordinate(
                                                    double.Parse(posList[i], CultureInfo.InvariantCulture),
                                                    double.Parse(posList[i + 1], CultureInfo.InvariantCulture));
                                                j++;
                                            }
                                            LinearRing geometry = factory.CreateLinearRing(coord);
                                            if (!geometry.IsValid)
                                            {
                                                Console.WriteLine($"Debug; Parcela nemá validní geometrii.");
                                                continue;
                                            }
                                            parcel.Geometry = geometry;
                                        }
                                    }
                                }
                                if (parcel.Area != 0)
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

        [AcRun.CommandMethod("BCTOOLSC_KN_AT")]
        public void Kn_DownloadAndAttachKn()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            Database db = document.Database;
            Editor editor = document.Editor;
            if (!db.TileMode)
            {
                editor.Warn("Povoleno pouze v modelovém prostoru.");
                return;
            }
            if (!Kn_TryDownloadData(editor, out string __saved, 
                "DXF", "DGN"))
                return;
            if (string.IsNullOrEmpty(__saved)) return;
            string ext = Path.GetExtension(__saved).Substring(1).ToUpper();
            if (ext == "DXF") Call(t =>
            {
                string logFilename = Path.Combine(BcApp.CurrentDirectory, "BcToolsC_conversion_dxf.log");
                using (var tb = new Database(false, true))
                {
                    tb.DxfIn(__saved, logFilename);
                    db.Insert(Matrix3d.Identity, tb, false);
                }
            });
            else
                document.SendStringToExecute("_.-DGNATTACH\n" +
                         $"\"{__saved}\"\n" +
                          "\n" +
                          "_Master\n" +
                          "*0,0,0\n" +
                          // ^ * stanovuje WCS souřadnice
                          "1\n" +
                          "0\n", true, false, false);
        }

        [AcRun.CommandMethod("BCTOOLSC_KN_DW")]
        public void Kn_DownloadKn()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            Database db = document.Database;
            Editor editor = document.Editor;
            if (!db.TileMode)
            {
                editor.Warn("Povoleno pouze v modelovém prostoru.");
                return;
            }
            if (!Kn_TryDownloadData(editor, out string __saved, 
                Kn_TypeThemeMap.Keys.ToArray()))
                return;
        }

        private bool Kn_TryDownloadData(Editor editor, out string lsFile,
            params string[] options)
        {
            lsFile = default;
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return false;
            }
            if (!ValidatePointInsideRelief(editor, __point.Value, out Point3d point)) return false;
            var __theme = GetKeywordFromPrompt(editor, "Vyberte formát", options);
            if (string.IsNullOrEmpty(__theme))
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return false;
            }
            if (Kn_TypeThemeMap.TryGetValue(__theme, out string theme))
            {
                var wgs84 = GetWGS84FromPoint(point);
                // Dotaz na ČÚZK
                AtomicEntries response = null;
                try
                {
                    string url = string.Format("https://atom.cuzk.cz/get.ashx?format=json&searchTerms=&theme={0}&crs=JTSK&bbox={1},{2},{1},{2}",
                        theme, wgs84.L, wgs84.B);
                    editor.Info("Kontaktuji ... https://atom.cuzk.cz");
                    string json = DownloadString(url);
                    if (string.IsNullOrWhiteSpace(json))
                        throw new Exception("Prázdná odpověď serveru.");
                    response = Deserialize<AtomicEntries>(json);
                }
                catch (Exception exception)
                {
                    editor.Error(exception.Message);
                    return false;
                }
                int n = response?.Entries?.Count ?? 0;
                if (response?.Entries == null || n == 0)
                {
                    editor.Warn("Nebyla nalazena žádná data.");
                    return false;
                }
                // Výběr entry, pokud je entries víc jak jeden,
                // uživatel je dotázán který konkrétní objekt chce
                AtomicEntries.Entry entry;
                if (n != 1)
                {
                    List<string> keywords = new List<string>();
                    for (int i = 0; i < n; i++)
                    {
                        AtomicEntries.Entry k = response.Entries[i];
                        var match = _knRegex.Match(k.Name);
                        if (match.Success)
                            keywords.Add(match.Groups[1].Value.Trim());
                    }
                    if (keywords.Count == 0)
                    {
                        editor.Warn("Nebyla nalazena žádná data.");
                        return false;
                    }
                    var __entry = GetKeywordFromPrompt(editor, "Výběr území", keywords.ToArray());
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                    entry = response.Entries.FirstOrDefault(e => e.Name.Contains(__entry));
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
                }
                else entry = response.Entries[0];
                if (entry == null)
                {
                    editor.Warn("Výběr byl zrušen uživatelem.");
                    return false;
                }
                CommonOpenFileDialog dialog = new CommonOpenFileDialog
                {
                    Title = "Vyber místo pro uložení souborů",
                    Multiselect = false,
                    ForceFileSystem = true,
                };
                string lsPath = BcApp.CurrentDirectory;
                if (!string.IsNullOrEmpty(BcApp.CurrentDirectory))
                    dialog.InputPath = BcApp.CurrentDirectory;
                if (dialog.ShowDialog() != true)
                {
                    editor.Warn("Výběr byl zrušen uživatelem.");
                    return false;
                }
                string knFile = Path.ChangeExtension(Path.GetFileName(new Uri(entry.Link).LocalPath), "." + __theme.ToLower());
                lsPath = dialog.ResultPath;
                if (!CanWrite(lsPath)) 
                {
                    editor.Warn("Adresář není zapisovatelný!");
                    return false;
                }
                // Kontrola jestli se soubor už v adresáři se stejným jménem nachází
                string knPath = Path.Combine(lsPath, knFile);
                if (File.Exists(knPath))
                {
                    MessageBoxResult result = MessageBox.Show("Soubor se už v adresáři nachází!", "Přepsat?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.No);
                    if (result != MessageBoxResult.Yes)
                    {
                        editor.Warn("Výběr byl zrušen uživatelem.");
                        return false;
                    }
                    // Soubor může být ještě zamčený
                    if (IsLocked(knPath))
                    {
                        MessageBox.Show(
                            "Soubor je právě používán jiným procesem nebo je zamčený pro zápis.",
                            "Soubor nelze přepsat",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return false;
                    }
                }
                var data = DownloadDataWithProgress(entry.Link);
                if (data == null || data.Length == 0)
                {
                    editor.Ok("Chyba; Nepovedlo se stáhnout data ve stanoveném čase.");
                    return false;
                }
                if (TryUnzipData(data, lsPath, out string __saved))
                {
                    lsFile = __saved;
                    return !string.IsNullOrEmpty(__saved);
                }    
            }
            return false;
        }
    }
}