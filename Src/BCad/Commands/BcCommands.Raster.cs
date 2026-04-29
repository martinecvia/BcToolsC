#pragma warning disable
using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.IO;
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
using BcToolsC.BCad.Transactions;

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

        [AcRun.CommandMethod("BCTOOLSC_TF_DW")]
        public void Tf_DownloadTiff()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
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
            var __theme = GetKeywordFromPrompt(editor, "Vyberte měřítko", Tf_TypeThemeMap.Keys.ToArray());
            if (string.IsNullOrEmpty(__theme) || !Tf_TypeThemeMap.TryGetValue(__theme, out string theme))
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            var wgs84 = GetWGS84FromPoint(point);

            // Stažení dat ze serveru ČÚZK
            AtomicEntries response = null;
            try
            {
                string url = string.Format("https://atom.cuzk.cz/get.ashx?format=json&searchTerms=&theme={0}&crs=JTSK&bbox={1},{2},{1},{2}",
                    theme, wgs84.L, wgs84.B);
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
            string tfwPath = Path.ChangeExtension(anyFile, ".tfw");
            string tifPath = Path.ChangeExtension(anyFile, ".tif");
            try
            {
                // Načtení a přečtení dat ze souboru TFW
                string[] tfwData = File.ReadAllLines(tfwPath);
                if (tfwData.Length < 6)
                {
                    editor.Error("Chyba; Souborová struktura není validní.");
                    return;
                }
                double[] tfw = ParseTfwData(tfwData);
                Call(t => ProcessRasterImage(t, editor, dir, tifPath, new CoordinateSystem3d(
                    new Point3d(tfw[4], tfw[5] + tfw[3], 0),
                    new Vector3d(tfw[0], +tfw[2], 0),
                    new Vector3d(tfw[1], -tfw[3], 0))));
            } catch (Exception exception)
            { editor.Error("Chyba; " + exception.Message); }
        }

        [AcRun.CommandMethod("BCTOOLSC_TF_ST")]
        public void Tf_SeatTiff()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;
            if (!ValidateDrawingPath(editor, out string dir)) return;
            if (!ValidateDirectoryWritable(editor, dir)) return;

            // Prompt pro výběr rastrových obrázků
            PromptSelectionOptions options = new PromptSelectionOptions { MessageForAdding = "Vyber obrázky pro zasazení" };
            PromptSelectionResult evResult = editor.GetSelection(options, new SelectionFilter(
                new[] { new TypedValue((int)DxfCode.Start, "IMAGE") }));
            if (evResult.Status != PromptStatus.OK)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            var selection = evResult.Value;
            if (selection == null || selection.Count == 0)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            int n = selection.Count;
            Call(t =>
            {
                for (int i = 0; i < n; i++)
                {
                    try { ProcessRasterImage(t, editor, dir, selection[i].ObjectId); }
                    catch (Exception exception)
                    { editor.Error("Chyba; " + exception.Message); }
                }
            });
        }

        private double[] ParseTfwData(string[] tfwData)
        {
            return new[]
            {
                double.Parse(tfwData[0], CultureInfo.InvariantCulture) * 10_000,
                double.Parse(tfwData[1], CultureInfo.InvariantCulture),
                double.Parse(tfwData[2], CultureInfo.InvariantCulture),
                double.Parse(tfwData[3], CultureInfo.InvariantCulture) * 10_000 * 0.8,
                double.Parse(tfwData[4], CultureInfo.InvariantCulture),
                double.Parse(tfwData[5], CultureInfo.InvariantCulture),
            };
        }

        private void ProcessRasterImage(BCadTransaction t, Editor editor,
            string dir, string key, CoordinateSystem3d orientation)
        {
            ObjectId dictId = RasterImageDef.GetImageDictionary(t.Database);
            if (!t.Exists(dictId))
                dictId = RasterImageDef.CreateImageDictionary(t.Database);
            if (!t.TryGet(dictId, out DBDictionary dict))
                throw new InvalidOperationException("Databáze rastrových obrázků není dostupná");
            string tifPath = ResolvePath(key, dir);
            key = Path.GetFileNameWithoutExtension(key);
            if (dict.Contains(key))
                return;

            // Získání potřebných cest
            if (string.IsNullOrEmpty(tifPath))
            {
                editor.Warn("Chyba; Nepovedlo se najít cestu k: " + key);
                return;
            }
            // Vytvoření rasterového obrázku
            var rasterDef = new RasterImageDef { SourceFileName = tifPath };
            rasterDef.Load();
            t.EnsureCanWrite(dict);
            ObjectId tiffId = dict.SetAt(key, rasterDef);
            t.Transaction.AddNewlyCreatedDBObject(rasterDef, true);
            using (var raster = new RasterImage
            { ImageDefId = tiffId, Orientation = orientation })
            {
                t.AddToModelSpace(raster);
                RasterImage.EnableReactors(true);
                rasterDef.Dispose();
                t.MoveToBottom(raster);
            }
            editor.Ok("Ok; Obrázek byl vložen.");
        }

        private void ProcessRasterImage(BCadTransaction t, Editor editor,
            string dir, ObjectId imageId)
        {
            if (!t.TryGet(imageId, out RasterImage raster, OpenMode.ForWrite))
                return;
            ObjectId rasterDefId = raster.ImageDefId;
            if (!t.TryGet(rasterDefId, out RasterImageDef rasterDef))
                return;

            // Potřebujeme zajistit že obrázek je správně načtený
            if (!rasterDef.IsLoaded)
            {
                t.EnsureCanWrite(rasterDef);
                rasterDef.Load();
            }

            // Získání potřebných cest
            string tifPath = ResolvePath(rasterDef.ActiveFileName, dir);
            if (string.IsNullOrEmpty(tifPath))
            {
                editor.Warn("Chyba; Nepovedlo se najít cestu k: " + raster?.Name);
                return;
            }
            string tfwPath = Path.ChangeExtension(tifPath, ".tfw");
            if (string.IsNullOrEmpty(tfwPath))
            {
                editor.Warn("Chyba; Nepovedlo se najít cestu k: " + raster?.Name);
                return;
            }

            // Načtení a přečtení dat ze souboru TFW
            string[] tfwData = File.ReadAllLines(tfwPath);
            if (tfwData.Length < 6)
            {
                editor.Error("Chyba; Souborová struktura není validní.");
                return;
            }
            double[] tfw = ParseTfwData(tfwData);
            raster.Orientation = new CoordinateSystem3d(
                new Point3d(tfw[4], tfw[5] + tfw[3], 0),
                new Vector3d(tfw[0], +tfw[2], 0),
                new Vector3d(tfw[1], -tfw[3], 0));
            t.MoveToBottom(raster);
            editor.Ok("Ok; Obrázek byl vložen.");
        }
    }
}