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
            if (!TryFetchAtomic("ZABAGED-vyskopis-DGN", wgs84, out AtomicEntries response))
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
            if (!TryFetchAtomic("CPX", wgs84, out AtomicEntries response))
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
            if (!TryFetchAtomic("KM-KU-DXF", wgs84, out AtomicEntries response))
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
            if (!TryFetchAtomic(theme, wgs84, out AtomicEntries response))
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
            using (XmlReader reader = xmlReader.ReadSubtree())
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    switch (reader.LocalName)
                    {
                        // Point
                        case "pos":
                            string pos = reader.ReadElementContentAsString();
                            if (string.IsNullOrEmpty(pos)) continue;
                            string[] posEntries = pos.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                            parcel.Point = new Point2d(
                                double.Parse(posEntries[0], CultureInfo.InvariantCulture),
                                double.Parse(posEntries[1], CultureInfo.InvariantCulture));
                            break;
                        // Geometry
                        case "posList":
                            string posList = reader.ReadElementContentAsString();
                            if (string.IsNullOrEmpty(posList)) continue;
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
                            if (string.IsNullOrEmpty(landUse)) continue;
                            if (Kn_LandUsesMap.TryGetValue(landUse, out string uses)) parcel.Uses = uses;
                            break;
                        // Town
                        // Tuid
                        case "administrativeUnit":
                            parcel.Town = reader.GetAttribute("xlink:title");
                            var administrativeUnit = reader.GetAttribute("xlink:href");
                            if (string.IsNullOrEmpty(administrativeUnit)) continue;
                            var match = _tnRegex.Match(administrativeUnit);
                            if (match.Success) parcel.Tuid = match.Groups[1].Value;
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
    }
}