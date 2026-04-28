using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Text.RegularExpressions;
using System.IO;
using System.Windows;
using System.Globalization;

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.EditorInput;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
#endif
#endregion

using static BcToolsC.BCad.Transactions.BCadTransaction;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly Dictionary<string, string> Tf_TypeThemeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "5000", "ZTM5-SJTSK-TIFF" },
            { "10000", "ZTM10-SJTSK-TIFF" },
            { "25000", "ZTM25-SJTSK-TIFF" },
            { "50000", "ZTM50-SJTSK-TIFF" },
            { "100000", "ZTM100-SJTSK-TIFF" },
            { "250000", "ZTM250-SJTSK-TIFF" },
        };

        [AcRun.CommandMethod("BCTOOLSC_TF_DOWN")]
        public void Tf_DownloadTiff()
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
            string lsPath = BcApp.CurrentDirectory;
            if (string.IsNullOrEmpty(lsPath))
            {
                editor.Error("Výkres musí být před použitím příkazu uložený!");
                return;
            }
            if (!CanWrite(lsPath)) goto addr_isnt_writable;
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null) goto user_closed_dialog;
            var __theme = GetKeywordFromPrompt(editor, "Vyberte měřítko", Tf_TypeThemeMap.Keys.ToArray());
            if (string.IsNullOrEmpty(__theme)) goto user_closed_dialog;
            var point = __point.Value.TransformBy(ucs);
            if (Tf_TypeThemeMap.TryGetValue(__theme, out string theme))
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
                    return;
                }
                int n = response?.Entries?.Count ?? 0;
                if (response?.Entries == null || n == 0) goto no_data;
                // Výběr entry, pokud je entries víc jak jeden,
                // uživatel je dotázán který konkrétní objekt chce
                AtomicEntries.Entry entry;
                if (n != 1)
                {
                    List<string> keywords = new List<string>();
                    for (int i = 0; i < n; i++)
                    {
                        AtomicEntries.Entry k = response.Entries[i];
                        keywords.Add(Regex.Split(k.Name, "mapový list: ")[1]);
                    }
                    if (keywords.Count == 0) goto no_data;
                    var __entry = GetKeywordFromPrompt(editor, "Vyberte mapový list", keywords.ToArray());
                    if (__entry == null) goto local_user_closed_dialog;
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                    entry = response.Entries.FirstOrDefault(e => e.Name.Contains(__entry));
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
                } else entry = response.Entries[0];
                if (entry == null) goto user_closed_dialog;
                string lsFile = Path.ChangeExtension(Path.GetFileName(new Uri(entry.Link).LocalPath), ".tfw");
                var tfwPath = Path.Combine(lsPath, lsFile);
                // Kontrola jestli se soubor už v adresáři se stejným jménem nachází
                if (File.Exists(tfwPath))
                {
                    MessageBoxResult result = MessageBox.Show("Soubor se už v adresáři nachází!", "Přepsat?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.No);
                    if (result != MessageBoxResult.Yes)
                        goto user_closed_dialog;
                    // Soubor může být ještě zamčený
                    if (IsLocked(tfwPath))
                    {
                        MessageBox.Show(
                            "Soubor je právě používán jiným procesem nebo je zamčený pro zápis.",
                            "Soubor nelze přepsat",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }
                var data = DownloadDataWithProgress(entry.Link);
                if (data == null || data.Length == 0)
                {
                    editor.Error("Chyba; Nepovedlo se stáhnout data ve stanoveném čase.");
                    return;
                }
                if (TryUnzipData(data, lsPath, 
                    out string __saved))
                {
                    try
                    {
                        var tifPath = Path.ChangeExtension(tfwPath, ".tif");
                        string[] tfwLines = File.ReadAllLines(tfwPath);
                        if (tfwLines.Length < 6)
                        {
                            editor.Error("Chyba; Souborová struktura není validní.");
                            return;
                        }
                        double[] tfw = new[]
                        {
                            double.Parse(tfwLines[0], CultureInfo.InvariantCulture) * 10_000,
                            double.Parse(tfwLines[1], CultureInfo.InvariantCulture),
                            double.Parse(tfwLines[2], CultureInfo.InvariantCulture),
                            double.Parse(tfwLines[3], CultureInfo.InvariantCulture) * 10_000 * 0.8,
                            double.Parse(tfwLines[4], CultureInfo.InvariantCulture),
                            double.Parse(tfwLines[5], CultureInfo.InvariantCulture),
                        };
                        Point3d origin = new Point3d(tfw[4], tfw[5] + tfw[3], 0);
                        var xVect = new Vector3d(tfw[0], +tfw[2], 0);
                        var yVect = new Vector3d(tfw[1], -tfw[3], 0);
                        // https://help.autodesk.com/view/OARX/2024/ENU/?guid=GUID-00A0BCD9-4519-4746-BC73-88544D75D789
                        var key = Path.GetFileNameWithoutExtension(__saved);
                        Call(t =>
                        {
                            ObjectId tiffId;
                            RasterImageDef raster;
                            ObjectId dictId = RasterImageDef.GetImageDictionary(t.Database);
                            if (!t.Exists(dictId))
                                dictId = RasterImageDef.CreateImageDictionary(t.Database);
                            if (!t.TryGet(dictId, out DBDictionary dict))
                                throw new InvalidOperationException("Databáze rastrových obrázků není dostupná");
                            if (dict.Contains(key)) return;
                            raster = new RasterImageDef { SourceFileName = tifPath };
                            raster.Load();
                            t.EnsureCanWrite(dict);
                            tiffId = dict.SetAt(key, raster);
                            t.Transaction.AddNewlyCreatedDBObject(raster, true);
                            using (var rasterImg = new RasterImage
                            {
                                ImageDefId = tiffId,
                                Orientation = new CoordinateSystem3d(origin, xVect, yVect)
                            })
                            {
                                t.AddToModelSpace(rasterImg);
                                RasterImage.EnableReactors(true);
                                raster.Dispose();
                                t.MoveToBottom(rasterImg);
                            }
                            editor.Ok("Ok; Vloženo");
                        });
                    } catch (Exception message) {
                        editor.Error("Chyba; " + message.Message);
                    }
                }
                else
                    editor.Ok("Chyba; Nepovedlo se uložit soubor.");
                return;
            local_user_closed_dialog:
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            return;
        no_data:
            editor.Warn("Nebyla nalazena žádná data.");
            return;
        user_closed_dialog:
            editor.Warn("Výběr byl zrušen uživatelem.");
            return;
        addr_isnt_writable:
            editor.Warn("Adresář není zapisovatelný!");
            return;
        }

        [AcRun.CommandMethod("BCTOOLSC_TF_SEAT")]
        public void Tf_ApplyTiff()
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
            string lsPath = BcApp.CurrentDirectory;
            if (string.IsNullOrEmpty(lsPath))
            {
                editor.Error("Výkres musí být před použitím příkazu uložený!");
                return;
            }
            if (!CanWrite(lsPath)) goto addr_isnt_writable;
            PromptSelectionResult evResult = editor.GetSelection(new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "IMAGE") }));
            if (evResult.Status != PromptStatus.OK) goto user_closed_dialog;
            var __value = evResult.Value;
            if (__value.Count == 0) goto no_data;
            Call(t =>
            {
                int n = __value.Count;
                for (int i = 0; i < n; i++)
                {
                    var s = __value[i];
                    try
                    {
                        if (!t.TryGet(s.ObjectId, out RasterImage rasterImg, OpenMode.ForWrite))
                            continue;
                        ObjectId tiffId = rasterImg.ImageDefId;
                        if (!t.TryGet(tiffId, out RasterImageDef raster))
                            continue;
                        if (!raster.IsLoaded)
                        {
                            t.EnsureCanWrite(raster);
                            raster.Load();
                        }
                        var tifPath = raster.ActiveFileName;
                        if (string.IsNullOrEmpty(tifPath))
                        {
                            editor.Warn("Chyba; Nepovedlo se najít cestu k: " + rasterImg?.Name);
                            continue;
                        }
                        string tfwPath;
                        if (Path.IsPathRooted(tifPath) && File.Exists(tifPath))
                            tfwPath = Path.ChangeExtension(tifPath, ".tfw");
                        // Cesta je relativní, musíme tedy nejprve získat cestu k obrázku
                        else
                        {
                            tifPath = Path.GetFullPath(Path.Combine(lsPath, tifPath));
                            if (File.Exists(tifPath))
                                tfwPath = Path.ChangeExtension(tifPath, ".tfw");
                            else
                                tfwPath = null;
                        }
                        // Kontrola nullability cest
                        if (string.IsNullOrEmpty(tfwPath))
                        {
                            editor.Warn("Chyba; Nepovedlo se najít cestu k: " + rasterImg?.Name);
                            continue;
                        }
                        string[] tfwLines = File.ReadAllLines(tfwPath);
                        if (tfwLines.Length < 6)
                        {
                            editor.Error("Chyba; Souborová struktura není validní.");
                            return;
                        }
                        double[] tfw = new[]
                        {
                            double.Parse(tfwLines[0], CultureInfo.InvariantCulture) * 10_000,
                            double.Parse(tfwLines[1], CultureInfo.InvariantCulture),
                            double.Parse(tfwLines[2], CultureInfo.InvariantCulture),
                            double.Parse(tfwLines[3], CultureInfo.InvariantCulture) * 10_000 * 0.8,
                            double.Parse(tfwLines[4], CultureInfo.InvariantCulture),
                            double.Parse(tfwLines[5], CultureInfo.InvariantCulture),
                        };
                        Point3d origin = new Point3d(tfw[4], tfw[5] + tfw[3], 0);
                        var xVect = new Vector3d(tfw[0], +tfw[2], 0);
                        var yVect = new Vector3d(tfw[1], -tfw[3], 0);
                        rasterImg.Orientation = new CoordinateSystem3d(origin, xVect, yVect);
                        t.MoveToBottom(rasterImg);
                        editor.Ok("Ok; Vloženo");
                    } catch (Exception exception) {
                        editor.Error("Chyba; " + exception.Message);
                    }
                }
            });
            return;
        no_data:
            editor.Warn("Nebyla nalazena žádná data.");
            return;
        addr_isnt_writable:
            editor.Warn("Adresář není zapisovatelný!");
            return;
        user_closed_dialog:
            editor.Warn("Výběr byl zrušen uživatelem.");
            return;
        }

        #region CALLING_O_TPL
        #endregion
    }