using System; // Keep for .NET 4.6
using System.Diagnostics;

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

using static BcToolsC.Helpers.KrovakHelper;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        [AcRun.CommandMethod("BCTOOLSC_MP_GM_STREET")]
        public void Mp_Map_GoogleMapsStreetView() => ShowMapFor("https://maps.google.com", "/maps?q=&layer=c&cbll={0},{1}");
        [AcRun.CommandMethod("BCTOOLSC_MP_GM")]
        public void Mp_Map_GoogleMaps() => ShowMapFor("https://www.google.com", "/maps/search/?api=1&query={0},{1}");
        [AcRun.CommandMethod("BCTOOLSC_MP_MC")]
        public void Mp_StreetView_Mapy() => ShowMapFor("https://mapy.com", "/fnc/v1/showmap?mapset=basic&center={1},{0}&zoom=16&marker=true");
        void ShowMapFor(string provider, string endpoint)
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }

            // Transformace do správného souřadnicového systému
            Matrix3d transform = editor.CurrentUserCoordinateSystem;
            Point3d point = __point.Value.TransformBy(transform);
            // Převedení S-JTSK souřadnic do WGS-84
            __4326 wgs84 = GetWGS84FromPoint(point);
            try
            {
                string url = string.Format(endpoint,
                    wgs84.B, wgs84.L);
                editor.Info("Kontaktuji ... " + provider);
                Process.Start(new ProcessStartInfo
                {
                    FileName = provider + url,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            { editor.Error("Chyba; " + exception.Message); }
        }
    }
}