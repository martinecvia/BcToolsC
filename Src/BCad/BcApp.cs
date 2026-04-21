#define NON_VOLATILE_MEMORY
using System; // Keep for .NET 4.6
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using System.IO;

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcadApplication = ZWCAD.ZcadApplication;
using AcadDocument = ZWCAD.ZcadDocument;

using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.ApplicationServices;
using AcDb = ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.EditorInput;
#else
using AcadApplication = Autodesk.AutoCAD.Interop.AcadApplication;
using AcadDocument = Autodesk.AutoCAD.Interop.AcadDocument;

using AcApp = Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.ApplicationServices;
using AcDb  = Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.EditorInput;
#endif
#endregion

using BcToolsC.BCad.Commands;
using BcToolsC.Models;

[assembly: CommandClass(typeof(BcToolsC.BCad.BcApp))]
namespace BcToolsC.BCad
{
    // Spouštìní aplikace z registru:
    // https://keanw.com/2015/01/using-environment-variables-inside-autocad-file-path-options.html
    public class BcApp : IExtensionApplication
    {
        public static string Name => "";
        public static string Version => $"BcToolsC.NET / 1.0.2604.19-test";
        public static AcadApplication ThisApplication => (AcadApplication)Application.AcadApplication;
        public static AcadDocument ThisDrawing => (AcadDocument)DocumentExtension.GetAcadDocument(Document);
        // Platforma, pro kterou máme spuštìnou instanci
        public static bool IsAcad { get; private set; }
        public static Document Document => AcApp.Core.Application.DocumentManager.MdiActiveDocument;
        public static RXClass Entity = RXObject.GetClass(typeof(AcDb.Entity));
        public static string CurrentDirectory => Document != null 
            ? Path.GetDirectoryName(Document.Database.Filename)
            : Path.GetTempPath();

        public void Initialize()
        {
            Document document = Document
                ?? throw new InvalidOperationException("not loaded yet!");
            Editor editor = document.Editor;
            AllocConsole();
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
#if !NET8_0_OR_GREATER
                // Starší verze naèítají tuhle knihovnu u nìkterých funkcií, a je viditelný "zásek", proto to loadíme co nejdøíve
                try { System.Reflection.Assembly.Load("Accessibility, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"); }
                catch (System.Exception exception) { editor.Warn($"Chyba naètení knihovny 'Accessibility'; Výjimka: {exception}"); }
#endif
                try
                {
                    var culture = (CultureInfo)CultureInfo.GetCultureInfo("cs-CZ").Clone();
                    culture.NumberFormat.NumberGroupSeparator = "";
                    culture.NumberFormat.NumberDecimalSeparator = ".";
                    CultureInfo.DefaultThreadCurrentCulture = culture;
                    CultureInfo.DefaultThreadCurrentUICulture = culture;
                }
                catch (CultureNotFoundException exception) { editor.Error($"Chyba naètení èeského prostøedí; Výjimka: {exception}"); }
                // Získání informace o aktuálním procesu
                if (Process.GetCurrentProcess().ProcessName.Contains("acad"))
                {
                    try
                    {
                        foreach (System.Reflection.Assembly assembly 
                            in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            var fullName = assembly.FullName;
                            // Zjistíme jestli naše aplikace má naètenou knihovnu "acdbmgd"
                            if (fullName != null && fullName.StartsWith("acdbmgd", StringComparison.OrdinalIgnoreCase))
                                IsAcad = true;
                        }
                    } 
                    catch (System.Exception exception)
                    {
                        editor.Error($"Získání informace o platformì selhalo; Výjimka: {exception}\n");
                    }
                }
                BcCommands.Rf_TypeArray = BcCommands.DeserializeFromBase64(ReliefRepository.COMPILE_RELIEF_DOUBLE_ARRAY);
                editor.WriteMessage("\n==========================================" +
                "\n   Návrh a realizace podpùrných nástrojù pro projektanty" +
                "\n   (c) 2026 Martin Coplák  |  VUT Brno" +
                "\n   Contact: Martin Coplák <martin.coplak@gmail.com>" +
                "\n   Consult: Ing. Michal Kosòovský, Ph.D. <michal.kosnovsky@vut.cz>" +
                "\n   Oponent: Ing. Jacek Wendrinski, Ph.D. <jacek.wendrinski@viapont.cz>" +
                "\n------------------------------------------" +
                "\n   BcToolsC.NET Version: " + Version +
                "\n==========================================\n");
                // Zde probíhá inicializace instance
                editor.Ok($"Inicializace dokonèena.\n");
            }
            catch (System.Exception exception)
            {
                editor.Error($"Inicializace selhala; Výjimka: {exception}\n");
            }
        }

        public void Terminate() 
        {

        }

        // syscall pro otevøeni konzole
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AllocConsole();
    }
}