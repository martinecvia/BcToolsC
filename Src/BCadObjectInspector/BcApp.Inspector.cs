using System; // Keep for .NET 4.6

#pragma warning disable
#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Windows;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Windows;
#endif
#endregion

[assembly: AcRun.CommandClass(typeof(BcToolsC.BCad.Inspector.BcAppInspector))]
namespace BcToolsC.BCad.Inspector
{
    public class BcAppInspector 
        : ContextMenuExtension
    {
        public static AcApp.Document Document => AcApp.Core.Application.DocumentManager.MdiActiveDocument;
        public BcAppInspector(string titleKey, params MenuItem[] items) 
        {
            if (string.IsNullOrEmpty(titleKey))
                throw new ArgumentException(titleKey);
            Title = titleKey;
            MenuItem hierarchy = new MenuItem(titleKey);
            hierarchy.Click += (_, __) => Document?.SendStringToExecute($"BCTOOLSC_IN\n", false, false, false);
            MenuItems.Add(hierarchy);
            foreach (MenuItem item in items)
            {
                if (!string.IsNullOrEmpty(item?.Text))
                    item.Click += (_, __) => Document?.SendStringToExecute($"BCTOOLSC_IN_{item.Text.ToUpperInvariant()}\n", false, false, false);
                MenuItems.Add(item);
            }
        }

        [AcRun.CommandMethod("BCTOOLSC_IN", 
         AcRun.CommandFlags.Modal | AcRun.CommandFlags.UsePickSet)]
        public void In_InspectSelected()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Editor editor = document.Editor;

            PromptSelectionResult evResult = editor.GetSelection();
            var selection = evResult.Value;
            if (evResult.Status != PromptStatus.OK ||
                selection.Count == 0)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            var val = new ObjectIdCollection(selection.GetObjectIds());
            DisplayInspector(editor, val);
        }

        [AcRun.CommandMethod("BCTOOLSC_IN_ENTITY",
         AcRun.CommandFlags.Modal)]
        public void In_InspectEntity()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Editor editor = document.Editor;

            PromptNestedEntityResult evResult = editor.GetNestedEntity("\nVyber vnořený objekt ...");
            if (evResult.Status != PromptStatus.OK)
            {
                editor.Warn("Nebyla nalazena žádná data.");
                return;
            }
            var val = evResult.ObjectId;
            DisplayInspector(editor, val);
        }

        [AcRun.CommandMethod("BCTOOLSC_IN_DATABASE",
         AcRun.CommandFlags.Modal)]
        public void In_InspectDatabase() 
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Editor editor = document.Editor;

            var val = HostApplicationServices.WorkingDatabase;
            DisplayInspector(editor, val);
        }

        [AcRun.CommandMethod("BCTOOLSC_IN_TABLE",
         AcRun.CommandFlags.Modal)]
        public void In_InspectTable()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = HostApplicationServices.WorkingDatabase;
            Editor editor = document.Editor;

            var val = new ObjectIdCollection
            {
                db.BlockTableId,
                db.DimStyleTableId,
                db.LayerTableId,
                db.LinetypeTableId,
                db.RegAppTableId,
                db.TextStyleTableId,
                db.UcsTableId,
                db.ViewTableId,
                db.ViewportTableId
            };
            DisplayInspector(editor, val);
        }

        [AcRun.CommandMethod("BCTOOLSC_IN_DICTIONARY",
         AcRun.CommandFlags.Modal)]
        public void In_InspectDictionary()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = HostApplicationServices.WorkingDatabase;
            Editor editor = document.Editor;

            var val = db.NamedObjectsDictionaryId;
            DisplayInspector(editor, val);
        }

        private void DisplayInspector(Editor editor, object val)
        {
            try
            {
                AcApp.Core.Application.ShowModalWindow(new Inspector(new ViewModel(val)));
                editor.Ok("Ok; Zobrazen inspektor.");
            } catch (Exception exception)
            { editor.Error("Chyba; " + exception.Message); }
        }
    }
}