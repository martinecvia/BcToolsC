#pragma warning disable CS8600, IDE0090
using System; // Keep for .NET 4.6
using System.IO; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Text.RegularExpressions;
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
using System.Windows;
using System.Net;

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

            public Point2dCollection Geometry { get; set; }
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
            AtomicEntries response = null;
            try
            {
                string url = string.Format("https://atom.cuzk.cz/get.ashx?format=json&searchTerms=&theme={0}&crs=JTSK&bbox={1},{2},{1},{2}",
                    "CP", wgs84.L, wgs84.B);
                Console.WriteLine(url);
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
            if (!TrySelectEntry(editor, response, _knRegex, out AtomicEntries.Entry entry))
                return;

            // Stažení dat
            byte[] data = DownloadDataWithProgress(entry.Link);
            if (data == null || data.Length == 0)
            {
                editor.Error("Chyba; Nepovedlo se stáhnout data ve stanoveném čase.");
                return;
            }
            var parcels = Kn_CollectParcels(data);
            int n = parcels.Count;
            if (n == 0)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }

            // Kontrola jestli jsme vevnitř
            AcParcel parcel = null;
            for (int i = 0; i < n; i++)
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
            string html = DownloadString("https://vdp.cuzk.gov.cz/vdp/ruian/parcely/" + parcel.Id);
            string obec = ExtractValue(html, "Obec:");
            string katUzemi = ExtractValue(html, "Katastrální území:");
            string druhPozemku = ExtractValue(html, "Druh pozemku:");
            string bpej = ExtractValue(html, "BPEJ");
            Call(t => t.AddLWPolyline(t.ConvertToPoint(parcel.Geometry), color: 1));
            MessageBox.Show(
                $"Obec: {obec}\n" +
                $"Katastrální území: {katUzemi}\n" +
                $"Kmenové číslo / poddělení: {parcel.Name}\n" +
                $"Výměra parcely [m2]: {parcel.Area}\n" +
                $"Druh pozemku: {druhPozemku}\n" +
                $"BPEJ: \n{bpej}",
                $"Parcela: {parcel.Id}",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string ExtractValue(string html, string label)
        {
            int labelIdx = html.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (labelIdx == -1) return "-";
            int tdStartIdx = html.IndexOf("<td", labelIdx, StringComparison.OrdinalIgnoreCase);
            if (tdStartIdx == -1) return "-";
            int tdEndIdx = html.IndexOf("</td>", tdStartIdx, StringComparison.OrdinalIgnoreCase);
            if (tdEndIdx == -1) return "-";
            string content = html.Substring(tdStartIdx, tdEndIdx - tdStartIdx);
            content = Regex.Replace(content, "<.*?>", string.Empty);
            content = WebUtility.HtmlDecode(content);
            content = Regex.Replace(content, @"\s+", " ").Trim();
            return content;
        }

        static List<AcParcel> Kn_CollectParcels(byte[] data)
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
                                    reader.GetAttribute("id", "http://www.opengis.net/gml/3.2").Substring(3),
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
                                            Point2dCollection geometry = new Point2dCollection();
                                            int j = 0;
                                            for (int i = 0; i + 1 < posList.Length; i += 2)
                                            {
                                                geometry.Add(new Point2d(
                                                    double.Parse(posList[i], CultureInfo.InvariantCulture),
                                                    double.Parse(posList[i + 1], CultureInfo.InvariantCulture)));
                                                j++;
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
                    "KM-KU-DXF", wgs84.L, wgs84.B);
                Console.WriteLine(url);
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
            var wgs84 = GetWGS84FromPoint(point);

            var __theme = GetKeywordFromPrompt(editor, "Vyberte formát", Kn_TypeThemeMap.Keys.ToArray());
            if (string.IsNullOrEmpty(__theme))
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            if (string.IsNullOrEmpty(__theme) || !Kn_TypeThemeMap.TryGetValue(__theme, out string theme))
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }

            // Stažení dat ze serveru ČÚZK
            AtomicEntries response = null;
            try
            {
                string url = string.Format("https://atom.cuzk.cz/get.ashx?format=json&searchTerms=&theme={0}&crs=JTSK&bbox={1},{2},{1},{2}",
                    theme, wgs84.L, wgs84.B);
                Console.WriteLine(url);
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
    }
}