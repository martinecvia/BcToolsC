using System; // Keep for .NET 4.6
using System.Diagnostics;

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
            if (Command.StartsWith("http"))
            {
#pragma warning disable CS8601 // Possible null reference assignment.
                if (!Uri.TryCreate(command, UriKind.RelativeOrAbsolute, out Uri _))
                    return;
#pragma warning restore CS8601 // Possible null reference assignment.
                Process.Start(new ProcessStartInfo
                {
                    FileName = command,
                    UseShellExecute = true
                });
                return;
            }
            if (string.IsNullOrWhiteSpace(command)) return;
            document?.SendStringToExecute(command + " ", true, false, false);
        }
    }
}