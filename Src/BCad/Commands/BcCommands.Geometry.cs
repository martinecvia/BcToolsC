#pragma warning disable
#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
#endif
#endregion

using BcToolsC.BCad.Commands.Models;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        [AcRun.CommandMethod("BCTOOLSC_GE_AC")]
        public void Ge_AlignTextToCurve()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;
            var __field = GetEntityFromPrompt(editor, "Vyberte text",
            typeof(DBText), typeof(MText));
            if (__field == ObjectId.Null)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            var __curve = GetEntityFromPrompt(editor, "Vyberte křivku",
            typeof(Line), typeof(Spline), typeof(Polyline3d), typeof(Polyline2d), typeof(Polyline),
            typeof(Arc), typeof(Circle), typeof(Ellipse));
            if (__curve == ObjectId.Null)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            using (var t = db.TransactionManager.StartTransaction())
            {
                var curve = t.GetObject(__curve, OpenMode.ForRead) as Curve;
                var field = t.GetObject(__field, OpenMode.ForWrite);
                if (curve == null || field == null)
                {
                    editor.Error("Chyba; Reference neodkazuje na objekt v databázi [E_MEMORY_INVALID].");
                    return;
                }
                var jig = new Ge_AlignTextToCurve(editor, curve, (Entity)field);
                PromptResult evResult = editor.Drag(jig);
                if (evResult.Status == PromptStatus.OK)
                {
                    t.Commit();
                    editor.Ok("Ok; Text byl zarovnán.");
                    return;
                }
                else
                {
                    t.Abort();
                    editor.Warn("Výběr byl zrušen uživatelem.");
                    return;
                }
            }
        }
    }
}