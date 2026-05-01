#pragma warning disable
#define NON_VOLATILE_MEMORY
using System; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using System.IO;

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcadApplication = ZWCAD.ZcadApplication;
using AcadDocument = ZWCAD.ZcadDocument;
using AcadUCS = ZWCAD.ZcadUCS;

using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.ApplicationServices;
using AcDb = ZwSoft.ZwCAD.DatabaseServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;

using ZwSoft.ZwCAD.Windows;
using ZwSoft.Windows;
#else
using AcadApplication = Autodesk.AutoCAD.Interop.AcadApplication;
using AcadDocument = Autodesk.AutoCAD.Interop.AcadDocument;
using AcadUCS = Autodesk.AutoCAD.Interop.Common.AcadUCS;

using AcApp = Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.ApplicationServices;
using AcDb  = Autodesk.AutoCAD.DatabaseServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

using Autodesk.AutoCAD.Windows;
using Autodesk.Windows;
#endif
#endregion

using BcToolsC.BCad.Commands;
using BcToolsC.Models;
using BcToolsC.Helpers;
using NetTopologySuite;
using BcToolsC.BCad.Inspector;

// RibbonXml
using AcRb = RibbonXml;
using RibbonXml;

[assembly: AcRun.CommandClass(typeof(BcToolsC.BCad.BcApp))]
namespace BcToolsC.BCad
{
    // Spouštění aplikace z registru:
    // https://keanw.com/2015/01/using-environment-variables-inside-autocad-file-path-options.html
    public class BcApp 
        : AcRun.IExtensionApplication
    {
        public static string Version => "BcToolsC.NET / 1.0.2605.01-release";
        public static AcadApplication ThisApplication => (AcadApplication)Application.AcadApplication;
        public static AcadDocument ThisDrawing => (AcadDocument)DocumentExtension.GetAcadDocument(Document);

        public static AcadUCS ThisUCS
        {
            get
            {
                try { return ThisDrawing?.ActiveUCS; }
                catch { return null; }
            }
        }

        // Platforma, pro kterou máme spuštěnou instanci
        public static bool IsAcad { get; private set; }
        public static Document Document => AcApp.Core.Application.DocumentManager.MdiActiveDocument;
        public static AcRun.RXClass Entity = AcRun.RXObject.GetClass(typeof(AcDb.Entity));
#pragma warning disable CS8603 // Possible null reference return.
        public static string CurrentDirectory
        {
            get
            {
                string lsFile = Document?.Database?.Filename;
                try
                {
                    if (!string.IsNullOrEmpty(lsFile) &&
                    // ZWCAD vrací AppData\Local\ZWSOFT\ZWCAD\202X\en-US\Template místo prázdého stringu
                    lsFile.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                    {
                        var dir = Path.GetDirectoryName(lsFile);
                        if (!string.IsNullOrEmpty(dir))
                            return dir;
                    }
                } catch (Exception exception)
                { Console.WriteLine(exception.Message); }
                return Path.GetTempPath();
            }
        }
#pragma warning restore CS8603 // Possible null reference return.
        public static bool IsAppProperlyInitialized { get; private set; }
        public static AcDb.Extents2d Envelope { get; private set; }
        public static AcRb.RibbonXml Ribbons { get; private set; }
        static BcAppInspector defaultInspector; // Default
        static BcAppInspector generalInspector; // Pro objekty jako takové

        public void Initialize()
        {
            Document document = Document
                ?? throw new InvalidOperationException("not loaded yet!");
            Editor editor = document.Editor;
#if DEBUG
            // Zobrazení pro vývojáře, nemělo by se objevit v produkci
            var pc = Environment.MachineName;
            if (string.Compare(pc, "MARTINCOPLKFB20", true) == 0 || string.Compare(pc, "PC-COPLAK2026",   true) == 0)
                AllocConsole();
#endif
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                AcRb.Builder hBuilder = new AcRb.Builder()
                    .SetDefaultHandler(typeof(BcAppRibbonCommandH));
                Assembly assembly = Assembly.GetExecutingAssembly();
                foreach (string resource in assembly.GetManifestResourceNames())
                    hBuilder.RegisterImage(resource.Split('.').Reverse().Skip(1).First(), resource);
                Ribbons = hBuilder.Build();
                Application.SystemVariableChanged += Application_SystemVariableChanged;
                if (ComponentManager.Ribbon == null)
                    ComponentManager.PropertyChanged += ComponentManager_PropertyChanged;
                else
                    RegisterRibbon();
#if !NET8_0_OR_GREATER
                // Starší verze načítají tuhle knihovnu u některých funkcií, a je viditelný "zásek", proto to loadíme co nejdříve
                try { System.Reflection.Assembly.Load("Accessibility, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"); }
                catch (System.Exception exception) { editor.Warn($"Chyba načtení knihovny 'Accessibility'; Výjimka: {exception}"); }
#endif
                try
                {
                    var culture = (CultureInfo)CultureInfo.GetCultureInfo("cs-CZ").Clone();
                    culture.NumberFormat.NumberGroupSeparator = "";
                    culture.NumberFormat.NumberDecimalSeparator = ".";
                    CultureInfo.DefaultThreadCurrentCulture = culture;
                    CultureInfo.DefaultThreadCurrentUICulture = culture;
                }
                catch (CultureNotFoundException exception) { editor.Error($"Chyba načtení českého prostředí; Výjimka: {exception}"); }
                // Získání informace o aktuálním procesu
                if (Process.GetCurrentProcess().ProcessName.Contains("acad"))
                {
                    try
                    {
                        foreach (System.Reflection.Assembly a 
                            in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            var fullName = a.FullName;
                            // Zjistíme jestli naše aplikace má načtenou knihovnu "acdbmgd"
                            if (fullName != null && fullName.StartsWith("acdbmgd", StringComparison.OrdinalIgnoreCase))
                                IsAcad = true;
                        }
                    } 
                    catch (System.Exception exception)
                    {
                        editor.Error($"Získání informace o platformě selhalo; Výjimka: {exception}\n");
                    }
                }
                var vertexes = CompressHelper.DeserializeFromBase64(ReliefRepository.COMPILE_RELIEF_DOUBLE_ARRAY_CZ);
                int rows = vertexes.GetLength(0);
                double minX = .0, maxX = .0;
                double minY = .0, maxY = .0;
                for (int i = 0; i < rows; i++)
                {
                    double x = vertexes[i, 0];
                    double y = vertexes[i, 1];
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
                Envelope = new AcDb.Extents2d(new Point2d(minX, minY), new Point2d(maxX, maxY));
                BcCommands.Rf_TypeArray_Cz = vertexes;
                NtsGeometryServices.Instance = new NtsGeometryServices(NetTopologySuite.Geometries.GeometryOverlay.NG);
                defaultInspector = new BcAppInspector("Inspektor", new MenuItem("Entity"), new MenuItem("Database"), new MenuItem("Table"), new MenuItem("Dictionary"));
                generalInspector = new BcAppInspector("Informace");
                AcApp.Application.AddDefaultContextMenuExtension(defaultInspector); AcApp.Application.AddObjectContextMenuExtension(Entity, generalInspector);
                editor.WriteMessage("\n==========================================" +
                "\n   Návrh a realizace podpůrných nástrojů pro projektanty" +
                "\n   (c) 2026 Martin Coplák  |  VUT Brno" +
                "\n   Contact: Martin Coplák <martin.coplak@gmail.com>" +
                "\n   Consult: Ing. Michal Kosňovský, Ph.D. <michal.kosnovsky@vut.cz>" +
                "\n   Oponent: Ing. Jacek Wendrinski, Ph.D. <jacek.wendrinski@viapont.cz>" +
                "\n------------------------------------------" +
                "\n   BcToolsC.NET Version: " + Version +
                "\n==========================================\n");
                // Zde probíhá inicializace instance
                editor.Ok($"Inicializace dokončena.\n");
                IsAppProperlyInitialized = true;
            }
            catch (System.Exception exception)
            {
                editor.Error($"Inicializace selhala; Výjimka: {exception.Message}\n");
            }
        }

        public void Terminate() 
        {
            if (defaultInspector != null) AcApp.Application.RemoveDefaultContextMenuExtension(defaultInspector);
            if (generalInspector != null)
                AcApp.Application.RemoveObjectContextMenuExtension(Entity, generalInspector);
        }

        private RibbonTab _tabWatcher = null;
        private void RegisterRibbon()
        {
            // https://keanw.com/wp-content/uploads/tp/WindowsLiveWriter/Ribbon%20layout.png
            _tabWatcher = Ribbons.CreateTab("rp_Default");
        }

        private void ComponentManager_PropertyChanged(object _, 
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Ribbon")
            {
                RegisterRibbon();
                ComponentManager.PropertyChanged -= ComponentManager_PropertyChanged;
            }
        }

        private void Application_SystemVariableChanged(object sender, 
            SystemVariableChangedEventArgs e)
        {
            // Při změně prostředí se může stát že ribbony zmizí
            if (e.Name == "WSCURRENT" && _tabWatcher != null)
            {
                if (!ComponentManager.Ribbon.Tabs.Contains(_tabWatcher))
                    ComponentManager.Ribbon.Tabs.Add(_tabWatcher);
            }
        }

        // syscall pro otevřeni konzole
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AllocConsole();
    }
}