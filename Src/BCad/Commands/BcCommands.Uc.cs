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
        [AcRun.CommandMethod("BCTOOLSC_CH_UCS")]
        public void _ChangeUcs()
        {
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Editor editor = document.Editor;
            if ((string)BcApp.ThisDrawing.GetVariable("UCSNAME") == "S-JTSK")
                return;
        }
    }
}