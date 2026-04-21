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

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly AcRun.RXClass _proxyEntity = AcRun.RXObject.GetClass(typeof(ProxyEntity));
        readonly AcRun.RXClass _proxyObject = AcRun.RXObject.GetClass(typeof(ProxyObject));

        [AcRun.CommandMethod("BCTOOLSC_MC_RM_PROXY")]
        public void _ClearProxy()
        {
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Editor editor = document.Editor;
            Database database = BcApp.Document.Database;
            long n = database.Handseed.Value / 100L;
            AcRun.ProgressMeter progress = new AcRun.ProgressMeter();
            progress.SetLimit(100);
            progress.Start("Procházím ...");
            // https://forums.autodesk.com/t5/net-forum/proxyobjects-amp-proxyentities-how-to-find-all/td-p/10867012
            var i = 0;
            Call(t => 
            {
                try
                {
                    var h = database.BlockTableId.Handle;
                    var l = h.Value;
                    while (true)
                    {
                        long p = l;
                        h = database.Handseed;
                        long c = h.Value;
                        if (p >= c) break;
                        if (l % n == 0L) progress.MeterProgress();
                        if (database.TryGetObjectId(new Handle(l), out ObjectId id) && !id.IsErased)
                        {
                            string regApp;
                            if (id.ObjectClass.IsDerivedFrom(_proxyEntity))
                            {
                                // ProxyEntity
                                if (t.TryGet(id, out ProxyEntity e, OpenMode.ForWrite) &&
                                    !e.IsErased &&
                                    (e.ProxyFlags & 1) == 1)
                                {
                                    regApp = GetPromptByRegApp(e.ApplicationDescription);
                                    if (!e.IsWriteEnabled) e.UpgradeOpen();
                                    e.Erase(true);
                                    ++i;
                                }
                            }
                            else if (id.ObjectClass.IsDerivedFrom(_proxyObject))
                            {
                                // ProxyObject
                                if (t.TryGet(id, out ProxyObject o, OpenMode.ForWrite) &&
                                    !o.IsErased &&
                                    (o.ProxyFlags & 1) == 1)
                                {
                                    regApp = GetPromptByRegApp(o.ApplicationDescription);
                                    if (!o.IsWriteEnabled) o.UpgradeOpen();
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
            editor.Info("Smazáno proxy objektů: " + i);
        }

        string GetPromptByRegApp(string regApp)
        {
            string[] parts = regApp.Split('|');
            if (parts.Length < 2)
                return regApp;
            if (parts[1].StartsWith("Product Desc: "))
                return $"{parts[0].TrimStart('"')} ({parts[1].Substring(13).Trim()})";
            return parts[0].Trim();
        }
    }
}