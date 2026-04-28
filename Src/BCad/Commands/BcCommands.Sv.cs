using System; // Keep for .NET 4.6
using System.Diagnostics;

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

using static BcToolsC.Helpers.KrovakHelper;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        [AcRun.CommandMethod("BCTOOLSC_SV")]
        public void Sv_StreetView()
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
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            var point = __point.Value;
            __4326 wgs84 = GetWGS84FromPoint(point);
            try
            {
                string url = string.Format("https://maps.google.com/maps?q=&layer=c&cbll={0},{1}",
                    wgs84.B, wgs84.L);
                editor.Info("Kontaktuji ... https://maps.google.com");
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true // Nutné pro zobrazení URL v browseru
                });
            }
            catch (Exception exception)
            {
                editor.Error(exception.Message);
                return;
            }
        }
    }
}