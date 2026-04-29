#region O_PROGRAM_DETERMINE_CAD_PLATFORM 
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
#endif
#endregion

namespace BcToolsC.BCad
{
    public class BcAppRibbonCommandH 
        : RibbonXml.CommandHandler
    {
        public BcAppRibbonCommandH(string command) : base(command)
        { }

        public override void Execute(object _)
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            string command = Command;
            if (string.IsNullOrWhiteSpace(command)) return;
            document?.SendStringToExecute(command + " ", true, false, false);
        }
    }
}