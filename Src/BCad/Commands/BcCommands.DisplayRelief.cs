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

using static BcToolsC.BCad.Transactions.BCadTransaction;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        internal static double[,] Rf_TypeArray_Cz = null;
        [AcRun.CommandMethod("BCTOOLSC_RF_CZ")]
        public void Rf_PrintRelief_Cz()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;

            // Vykreslení reliéfu
            const short COLOR = 3;
            Polyline polyline = Wrap(t => t.AddLWPolyline(Rf_TypeArray_Cz, color: COLOR, shouldBeClosed: true));
            if (polyline == null) 
            {
                editor.Error("Chyba; Deserializace neproběhla [E_MEMORY_INVALID].");
                return;
            }
            // Zoom do výkresu, zobrazující reliéf
            Extents3d extents = polyline.GeometricExtents;
            if (TryZoomToExtents(editor, extents))
                editor.Ok("Ok; Vykreslen reliéf ČR");
        }
    }
}