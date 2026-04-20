using System; // Keep for .NET 4.6
using System.IO; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Text;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Net;

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Windows;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Windows;
#endif
#endregion

using BcToolsC.Models;
using static BcToolsC.Helpers.KrovakHelper;

[assembly: AcRun.CommandClass(typeof(BcToolsC.BCad.Commands.ParcelCommands))]
namespace BcToolsC.BCad.Commands
{
    public class ParcelCommands
    {
        readonly Regex _kuRegex = new Regex(@":\s*(.*?)\s*\[",RegexOptions.Compiled | RegexOptions.IgnoreCase);
        readonly Dictionary<string, string> TypeThemeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DGN", "KM-KU-DGN" },
            { "DXF", "KM-KU-DXF" },
            { "VFK", "KM-KU-VFK" },
            { "SHP", "KM-KU-SHP" },
            { "VKM", "KM-KU-VKM" }
        };

        [DataContract] class Response_KM_KU
        {
            [DataMember(Name = "entry", IsRequired = false, EmitDefaultValue = false)]
            public List<KM_KU> Entries { get; set; }
            [DataContract] public class KM_KU
            {
                [DataMember(Name = "id")]
                public string Link { get; set; }

                [DataMember(Name = "title")]
                public string Name { get; set; }
            }
        }

        [AcRun.CommandMethod("BCTOOLSC_DW_KN")]
        public void _DownloadParcels()
        {
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Editor editor = document.Editor;
            var __point = GetPointFromPrompt  (editor, "Vyberte bod v modelovém prostoru");
            if (__point == null) goto user_closed_dialog;
            var __theme = GetKeywordFromPrompt(editor, "Vyberte formát", TypeThemeMap.Keys.ToArray());
            if (string.IsNullOrEmpty(__theme)) goto user_closed_dialog;
            var point = __point.Value;
            if (TypeThemeMap.TryGetValue(__theme, out string theme))
            {
                var wgs84 = GetWGS84FromPoint(point);
                Response_KM_KU response = null;
                try
                {
                    string url = string.Format("https://atom.cuzk.cz/get.ashx?format=json&searchTerms=&theme={0}&crs=JTSK&bbox={1},{2},{1},{2}",
                        theme, wgs84.L, wgs84.B);
                    editor.Debug(url);
                    string json = DownloadString(url);
                    response = Deserialize<Response_KM_KU>(json);
                }
                catch (Exception exception)
                {
                    editor.Error(exception.Message);
                    return;
                }
                int n = response?.Entries?.Count ?? 0;
                if (response?.Entries == null || n == 0) goto no_data;
                List<string> keywords = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    Response_KM_KU.KM_KU k = response.Entries[i];
                    var match = _kuRegex.Match(k.Name);
                    if (match.Success)
                        keywords.Add(match.Groups[1].Value.Trim());
                }
                if (keywords.Count == 0) goto no_data;
                var __entry = GetKeywordFromPrompt(editor, "Výběr území", keywords.ToArray());
                Response_KM_KU.KM_KU entry = response.Entries.FirstOrDefault(e => e.Name.Contains(__entry));
                if (entry == null) goto user_closed_dialog;
                string fileName = Path.GetFileName(new Uri(entry.Link).LocalPath);
                SaveFileDialog dialog = new SaveFileDialog("Vyber místo uložení", "fileName", __theme, "Vyberte místo uložení",
                SaveFileDialog.SaveFileDialogFlags.DoNotTransferRemoteFiles);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) goto user_closed_dialog;
                DownloadFile(entry.Link, dialog.Filename);
                editor.Ok($"Soubor byl uložen: {dialog.Filename}");
            }
            return;
        no_data:
            editor.Ok("Nebyli nalazeny žádné data.");
            return;
        user_closed_dialog:
            editor.Warn("Výběr byl zrušen uživatelem.");
            return;
        }

        T Deserialize<T>(string json)
        {
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(ms);
            }
        }

        void DownloadFile(string url, string lsPath, double timeout = 30.0)
        {
            using (TimeoutedWebClient wc = new TimeoutedWebClient { Timeout = (int)timeout * 1000 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] =
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36";
                wc.DownloadFile(url, lsPath);
            }
        }

        string DownloadString(string url, double timeout = 5.0)
        {
            using (TimeoutedWebClient wc = new TimeoutedWebClient { Timeout = (int)timeout * 1000})
            {
                wc.Headers[HttpRequestHeader.UserAgent] =
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36";
                return wc.DownloadString(url);
            }
        }

        string GetKeywordFromPrompt(Editor editor, string prompt, 
            params string[] argv)
        {
            if (argv.Length == 0) return null;
            PromptKeywordOptions format = new PromptKeywordOptions($"\n{prompt}: ") { AllowNone = false, };
            for (int i = 0; i < argv.Length; i++)
            {
                string k = argv[i];
                format.Keywords.Add(k.Replace(" ", "\u3164"));
            }
            PromptResult evResult = editor.GetKeywords(format);
            if (evResult.Status != PromptStatus.OK) return null;
            return evResult.StringResult.Replace("\u3164", " ");
        }

        Point3d? GetPointFromPrompt(Editor editor, string prompt)
        {
            PromptPointResult evResult = editor.GetPoint(new PromptPointOptions($"\n{prompt}: "));
            if (evResult.Status != PromptStatus.OK) return null;
            return evResult.Value;
        }

        __4326 GetWGS84FromPoint(Point3d point)
        {
            // S-JTSK pracuje v opačném kvadrantu, proto jsou tyto data prohozeny.
            double x = point.Y;
            double y = point.X;
            Console.WriteLine(string.Format("X:{0:0.00m}, Y:{1:0.00}", x, y));
            double z = point.Z;
            __4326 epsg;
            if (z > 0)
                epsg = SJTSK_WGS84(x, y, z);
            else
                epsg = SJTSK_WGS84(x, y);
            return epsg;
        }
    }
}