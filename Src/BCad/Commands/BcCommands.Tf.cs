using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Text.RegularExpressions;
using System.IO;
using System.Windows;

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.EditorInput;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.EditorInput;
#endif
#endregion

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

        [AcRun.CommandMethod("BCTOOLSC_TF_TIFF")]
        public void _DownloadTiff()
        {
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Editor editor = document.Editor;
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
            var point = __point.Value;
            if (Tf_TypeThemeMap.TryGetValue(__theme, out string theme))
            {
                var wgs84 = GetWGS84FromPoint(point);
                // Dotaz na ČÚZK
                AtomicEntries response = null;
                try
                {
                    string url = string.Format("https://atom.cuzk.cz/get.ashx?format=json&searchTerms=&theme={0}&crs=JTSK&bbox={1},{2},{1},{2}",
                        theme, wgs84.L, wgs84.B);
                    editor.Debug(url);
                    string json = DownloadString(url);
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
                    var __entry = GetKeywordFromPrompt(editor, "Výběr území", keywords.ToArray());
                    entry = response.Entries.FirstOrDefault(e => e.Name.Contains(__entry));
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
                }
                var data = DownloadData(entry.Link);
                if (TryUnzipData(data, lsPath, out string __saved))
                {
                    var tifPath = Path.ChangeExtension(__saved, ".tif");
                }
                else
                    editor.Ok("Chyba; Nepovedlo se uložit soubor.");
            }
            return;
        no_data:
            editor.Warn("Nebyli nalazeny žádné data.");
            return;
        user_closed_dialog:
            editor.Warn("Výběr byl zrušen uživatelem mezi monitorem a židlí.");
            return;
        addr_isnt_writable:
            editor.Warn("Adresář není zapisovatelný!");
            return;
        }
    }
}