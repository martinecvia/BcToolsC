#pragma warning disable
using System; // Keep for .NET 4.6

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

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        [AcRun.CommandMethod("BCTOOLSC_UC_UCS")]
        public void Uc_ChangeUcs()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;
            if ((string)BcApp.ThisDrawing.GetVariable("UCSNAME") == "S-JTSK") return;
            BcApp.ThisDrawing.ActiveUCS = BcApp.ThisDrawing.UserCoordinateSystems.Add(
                new[] { .0, .0, .0 },
                new[] { -1.0, .0, .0 },
                new[] { .0, -1.0, .0 },
                "S-JTSK");
            editor.Ok("Ok; Nastaven ucs na: SJTSK.");
        }

        [AcRun.CommandMethod("BCTOOLSC_UC_WCS")]
        public void Uc_ChangeWcs()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;
            editor.CurrentUserCoordinateSystem = Matrix3d.Identity;
            editor.Ok("Ok; Nastaven ucs na: WORLD.");
        }
    }
}