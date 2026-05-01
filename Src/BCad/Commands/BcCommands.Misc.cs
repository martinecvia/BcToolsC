using System; // Keep for .NET 4.6

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
using BcToolsC.BCad.Commands.Models;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly AcRun.RXClass _proxyEntity = AcRun.RXObject.GetClass(typeof(ProxyEntity));
        readonly AcRun.RXClass _proxyObject = AcRun.RXObject.GetClass(typeof(ProxyObject));

        [AcRun.CommandMethod("BCTOOLSC_MC_RM_PROXY")]
        public void Mc_ClearProxy()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;

            long n = db.Handseed.Value / 100L;
            if (n == 0) n = 1;
            using (AcRun.ProgressMeter progress = new AcRun.ProgressMeter())
            {
                progress.SetLimit(100);
                progress.Start("Procházím ...");
                // https://forums.autodesk.com/t5/net-forum/proxyobjects-amp-proxyentities-how-to-find-all/td-p/10867012
                var i = 0;
                Call(t =>
                {
                    try
                    {
                        var h = db.BlockTableId.Handle;
                        var l = h.Value;
                        while (true)
                        {
                            long p = l;
                            h = db.Handseed;
                            long c = h.Value;
                            if (p >= c) break;
                            if (l % n == 0L) progress.MeterProgress();
                            if (db.TryGetObjectId(new Handle(l), out ObjectId id) && !id.IsErased)
                            {
                                if (id.ObjectClass.IsDerivedFrom(_proxyEntity))
                                {
                                    // ProxyEntity
                                    if (t.TryGet(id, out ProxyEntity e, OpenMode.ForWrite) && !e.IsErased && (e.ProxyFlags & 1) == 1)
                                    {
                                        e.Erase(true);
                                        ++i;
                                    }
                                }
                                else if (id.ObjectClass.IsDerivedFrom(_proxyObject))
                                {
                                    // ProxyObject
                                    if (t.TryGet(id, out ProxyObject o, OpenMode.ForWrite) && !o.IsErased && (o.ProxyFlags & 1) == 1)
                                    {
                                        o.Erase(true);
                                        ++i;
                                    }
                                }
                            }
                            ++l;
                        }
                    }
                    catch (Exception)
                    { }
                });
                progress.Stop();
                editor.Ok("Ok; Smazaných proxy objektů: " + i);
            }
        }

        [AcRun.CommandMethod("BCTOOLSC_MC_AC")]
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