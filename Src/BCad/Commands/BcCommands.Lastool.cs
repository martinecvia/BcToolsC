using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.IO;

using System.Diagnostics;
using System.Windows;

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.EditorInput;

using ZwSoft.ZwCAD.Windows;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;

using Autodesk.AutoCAD.Windows;
#endif
#endregion

using static BcToolsC.BCad.Transactions.BCadTransaction;
using BcToolsC.Models;
using BcToolsC.BCad.Commands.Models;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly Dictionary<string, string> Lz_TypeThemeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DMR5G", "DMR5G-SJTSK" },
            { "DMR4G", "DMR4G-SJTSK" },
            { "DMP1G", "DMP1G-SJTSK" },
        };

        [AcRun.CommandMethod("BCTOOLSC_LZ_DW")]
        public void Lz_DownloadLast()
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
            var __theme = GetKeywordFromPrompt(editor, "Vyberte typ dat", Lz_TypeThemeMap.Keys.ToArray());
            if (string.IsNullOrEmpty(__theme) || !Lz_TypeThemeMap.TryGetValue(__theme, out string theme))
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
            if (!TrySelectEntry(editor, response, "mapový list: ", out AtomicEntries.Entry entry))
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

        [AcRun.CommandMethod("BCTOOLSC_LZ_AT")]
        public void Lz_DownloadAndAttachLz()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateLastoolInstall(editor, out string exePath)) return;
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
            if (!TryFetchAtomic("DMR5G-SJTSK", wgs84, out AtomicEntries response))
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
            if (!TryUnzipData(data, dir, ".laz", out string anyFile))
            {
                editor.Error("Chyba; Nepovedlo se uložit soubor.");
                return;
            }

            // Změna cest, soubory můžou mít jiné jméno souborů než je název archivu
            string lazPath = Path.ChangeExtension(anyFile, ".laz");
            string txtPath = Path.ChangeExtension(anyFile, ".txt");
            // Spuštění programu, pro následnou konverzi
            try
            {
                using (var process = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"-i \"{lazPath}\" -o \"{txtPath}\" -parse xyz -sep space",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    string stdErr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0 || !File.Exists(txtPath)) 
                    {
                        MessageBox.Show(
                            string.IsNullOrEmpty(stdErr) ? "[File.IO] Failed to export file in time, .laz is missing required headers" : stdErr,
                            "las2txt: StandardError",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }
                } 
            } catch (Exception exception)
            { editor.Error("Chyba; " + exception.Message); return; }
            
            // Vložení dat do výkresu
            if (!TryProcessCoordinatesFile(txtPath))
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
        }

        [AcRun.CommandMethod("BCTOOLSC_LZ_AN")]
        public void Lz_AttachLz()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;

            // Výběr místa uložení
            OpenFileDialog dialog = new OpenFileDialog("Vyber soubor .txt", null, "txt", nameof(Lz_AttachLz),
                OpenFileDialog.OpenFileDialogFlags.DoNotTransferRemoteFiles);
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }

            // Zjištění velikosti souboru
            long length = 0;
            try
            {
                var fi = new FileInfo(dialog.Filename);
                if (fi.Exists) length = fi.Length;
            } catch (Exception exception)
            { editor.Error("Chyba; " + exception.Message); return; }
            if (length == 0)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }

            string txtPath = dialog.Filename;

            // Pokud o zjištění separátoru ze souboru, vybereme místo v 1/3 velikosti,
            // abychom obešli hlavičky
            if (!TryGetSeparatorFromFile(txtPath, (long)(length * 1/3), out char separator))
            {
                editor.Warn("Nepodařilo se určit formát souboru.");
                return;
            }

            // Vložení dat do výkresu
            if (!TryProcessCoordinatesFile(txtPath, separator))
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
        }

        static bool TryGetSeparatorFromFile(string lsFile, long offset, 
            out char separator)
        {
            separator = default;
            string line;
            try
            {
                var fi = new FileInfo(lsFile);
                if (!fi.Exists || fi.Length == 0) return false;
                using (var fs = new FileStream(lsFile, FileMode.Open, FileAccess.Read))
                using (var reader = new StreamReader(fs))
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    reader.DiscardBufferedData();

                    // Skipni řádek
                    if (offset > 0) 
                        reader.ReadLine();
                    line = reader.ReadLine();
                }
            } catch { return false; }
            if (string.IsNullOrEmpty(line)) return false;

            // Výběr separátoru podle toho jestli jeho podělením vznikne dostatek částí
            // Vhodné separátory jsou pak seřazeny podle toho jak nejméně se používají,
            // tím dosáhneme co nejméně false flagů
            char[] separators = { '\t', '|', ';', ' ', ',' };
            foreach (char s in separators)
            {
                string[] parts = line.Trim().Split(new[] { s }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    separator = s;
                    return true;
                }
            }
            return false;
        }

        static bool TryProcessCoordinatesFile(string lsFile,
            char separator = ' ')
        {
            var fi = new FileInfo(lsFile);
            if (!fi.Exists || fi.Length == 0) return false;
            long size = fi.Length;
            bool hasSize = size > 0;
            using (AcRun.ProgressMeter progress = new AcRun.ProgressMeter())
            {
                progress.SetLimit(100);
                progress.Start("Buduji síť ...");
                using (var fs = new FileStream(lsFile, FileMode.Open, FileAccess.Read))
                using (var reader = new StreamReader(fs))
                {
                    long totalRead = 0;
                    int last = 0;
                    Call(t =>
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            totalRead = fs.Position;
                            if (hasSize)
                            {
                                int curr = (int)((double)totalRead / size * 100);
                                if (curr > last)
                                {
                                    for (int i = 0; i < curr - last; i++)
                                        progress.MeterProgress();
                                    last = curr;
#pragma warning disable CA1416 // Validate platform compatibility
                                    System.Windows.Forms.Application.DoEvents();
#pragma warning restore CA1416 // Validate platform compatibility
                                }
                            }
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            string[] parts = line.Trim().Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
                            if (TryParsePoint(parts, out double x, out double y, out double z))
                                t.AddPoint(x, y, z);
                        }
                    });
                }
                progress.Stop();
                return true;
            }
        }

        private static bool TryParsePoint(string[] parts,
            out double x, out double y, out double z)
        {
            x = y = z = 0;
            if (parts == null || parts.Length < 3) return false;
            double d1 = ReadDouble(parts[1]);
            if (d1 == 0.0) return false;

            double? m = null;
            double d2 = ReadDouble(parts[2]);
            if (parts.Length >= 4) m = ReadDouble(parts[3]);
            if (m != null)
            {
                // Znaménka x,y se nezhodují
                if (Math.Sign(d1) != Math.Sign(d2)) return false;

                x = -Math.Abs(d1);
                y = -Math.Abs(d2);
                if (x < y)
                {
                    double tmp = x;
                    x = y;
                    y = tmp;
                }
                z = Math.Abs(m.Value);
                return true;
            } else {
                if (InsideRelief(d1, d2, out x, out y))
                {
                    z = 0.0;
                    return true;
                }
                else
                {
                    double d0 = ReadDouble(parts[0]);

                    // Znaménka x,y se nezhodují
                    if (Math.Sign(d0) != Math.Sign(d1)) return false;

                    x = -Math.Abs(d0);
                    y = -Math.Abs(d1);
                    if (x < y)
                    {
                        double tmp = x;
                        x = y;
                        y = tmp;
                    }
                    z = Math.Abs(d2);
                    return true;
                }
            }
        }
    }
}