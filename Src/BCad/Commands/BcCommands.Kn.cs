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
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
#endif
#endregion

using BcToolsC.Models;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly Regex _knRegex = new Regex(@":\s*(.*?)\s*\[",RegexOptions.Compiled | RegexOptions.IgnoreCase);
        readonly Dictionary<string, string> Kn_TypeThemeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DGN", "KM-KU-DGN" },
            { "DXF", "KM-KU-DXF" },
            { "VFK", "KM-KU-VFK" },
            { "SHP", "KM-KU-SHP" },
            { "VKM", "KM-KU-VKM" }
        };

        [AcRun.CommandMethod("BCTOOLSC_KN_DW")]
        public void Ku_DownloadKn()
        {
            AcApp.Document document = BcApp.Document;
            Database db = document.Database;
            Editor editor = document.Editor;
            if (!db.TileMode)
            {
                editor.Warn("Povoleno pouze v modelovém prostoru.");
                return;
            }
            var __point = GetPointFromPrompt  (editor, "Vyberte bod v modelovém prostoru");
            if (__point == null) goto user_closed_dialog;
            var __theme = GetKeywordFromPrompt(editor, "Vyberte formát", Kn_TypeThemeMap.Keys.ToArray());
            if (string.IsNullOrEmpty(__theme)) goto user_closed_dialog;
            var point = __point.Value;
            if (Kn_TypeThemeMap.TryGetValue(__theme, out string theme))
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
                        var match = _knRegex.Match(k.Name);
                        if (match.Success)
                            keywords.Add(match.Groups[1].Value.Trim());
                    }
                    if (keywords.Count == 0) goto no_data;
                    var __entry = GetKeywordFromPrompt(editor, "Výběr území", keywords.ToArray());
                    entry = response.Entries.FirstOrDefault(e => e.Name.Contains(__entry));
                }
                else entry = response.Entries[0];
                if (entry == null) goto user_closed_dialog;
                CommonOpenFileDialog dialog = new CommonOpenFileDialog
                {
                    Title = "Vyber místo pro uložení souborů",
                    Multiselect = false,
                    ForceFileSystem = true,
                };
                string lsPath = BcApp.CurrentDirectory;
                if (!string.IsNullOrEmpty(BcApp.CurrentDirectory))
                    dialog.InputPath = BcApp.CurrentDirectory;
                if (dialog.ShowDialog() != true) goto user_closed_dialog;
                string lsFile = Path.ChangeExtension(Path.GetFileName(new Uri(entry.Link).LocalPath), "." + __theme.ToLower());
                lsPath = dialog.ResultPath;
                if (!CanWrite(lsPath)) goto addr_isnt_writable;
                // Kontrola jestli se soubor už v adresáři se stejným jménem nachází
                if (File.Exists(Path.Combine(lsPath, lsFile)))
                {
                    MessageBoxResult result = MessageBox.Show("Soubor se už v adresáři nachází!", "Přepsat?", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question, 
                        MessageBoxResult.No);
                    if (result != MessageBoxResult.Yes)
                        goto user_closed_dialog;
                }
                var data = DownloadDataWithProgress(entry.Link);
                if (data == null || data.Length == 0)
                {
                    editor.Ok("Chyba; Nepovedlo se stáhnout data ve stanoveném čase.");
                    return;
                }
                if (TryUnzipData(data, lsPath, out string __saved))
                    editor.Ok("Ok; Cesta k souboru: " + __saved);
                else
                    editor.Ok("Chyba; Nepovedlo se uložit soubor.");
            }
            return;
        no_data:
            editor.Warn("Nebyla nalazena žádná data.");
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