using System; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM 
#if ZWCAD
using ZwSoft.ZwCAD.DatabaseServices;
#else
using Autodesk.AutoCAD.DatabaseServices;
#endif
#endregion

namespace BcToolsC.BCad.Transactions
{
    public partial class BCadTransaction
    {
        // https://help.autodesk.com/view/OARX/2026/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_LayerTableRecord
        public LayerTableRecord GetOrAdd_Layer(string name, Action<LayerTableRecord> defaults = null) =>
            GetOrAddRecord(name, () => new LayerTableRecord
            {
                // Global ...
            }, Database.LayerTableId, defaults);

        // https://help.autodesk.com/view/OARX/2026/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_LinetypeTableRecord
        public LinetypeTableRecord GetOrAdd_Linetype(string name, Action<LinetypeTableRecord> defaults = null) =>
            GetOrAddRecord(name, () => new LinetypeTableRecord
            {
                // Global ...
            }, Database.LinetypeTableId, defaults);

        // https://help.autodesk.com/view/OARX/2026/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_DimStyleTableRecord
        public DimStyleTableRecord GetOrAdd_DimStyle(string name, System.Action<DimStyleTableRecord> defaults = null) =>
            GetOrAddRecord(name, () => new DimStyleTableRecord
            {
                // Global ...
            }, Database.DimStyleTableId, defaults);

        // Nemám tušení k čemu se to používá
        // https://help.autodesk.com/view/OARX/2026/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_RegAppTableRecord
        public RegAppTableRecord GetOrAdd_RegApp(string name, Action<RegAppTableRecord> defaults = null) =>
            GetOrAddRecord(name, () => new RegAppTableRecord
            {
                // Global ...
            }, Database.RegAppTableId, defaults);

        // https://help.autodesk.com/view/OARX/2026/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_ViewportTableRecord
        public ViewportTableRecord GetOrAdd_Viewport(string name, Action<ViewportTableRecord> defaults = null) =>
            GetOrAddRecord(name, () => new ViewportTableRecord
            {
                // Global ...
            }, Database.ViewportTableId, defaults);

        // https://help.autodesk.com/view/OARX/2026/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_UcsTableRecord
        public UcsTableRecord GetOrAdd_Ucs(string name, Action<UcsTableRecord> defaults = null) =>
            GetOrAddRecord(name, () => new UcsTableRecord
            {
                // Global ...
            }, Database.UcsTableId, defaults);

        // https://help.autodesk.com/view/OARX/2026/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_ViewTableRecord
        public ViewTableRecord GetOrAdd_View(string name, Action<ViewTableRecord> defaults = null) =>
            GetOrAddRecord(name, () => new ViewTableRecord
            {
                // Global ...
            }, Database.ViewTableId, defaults);

        // https://help.autodesk.com/view/OARX/2026/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_TextStyleTableRecord
        public TextStyleTableRecord GetOrAdd_TextStyle(string name, Action<TextStyleTableRecord> defaults = null) =>
            GetOrAddRecord(name, () => new TextStyleTableRecord
            {
                // Global ...
            }, Database.TextStyleTableId, defaults);

        // https://help.autodesk.com/view/OARX/2026/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_BlockTableRecord
        public BlockTableRecord GetOrAdd_Block(string name, Action<BlockTableRecord> defaults = null) =>
            GetOrAddRecord(name, () => new BlockTableRecord
            {
                // Global ...
            }, Database.BlockTableId, defaults);

        bool HasRecord(string name, ObjectId objectId)
        {
            SymbolTable table = Get<SymbolTable>(objectId);
            return table != null && table.Has(name);
        }

        private T GetOrAddRecord<T>(string name, Func<T> f, ObjectId objectId,
            Action<T> defaults = null)
            where T : SymbolTableRecord
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name cannot be null or empty.", nameof(name));
            if (f == null) throw new ArgumentNullException(nameof(f));
            SymbolTable table = Get<SymbolTable>(objectId);
            if (table == null)
                throw new InvalidOperationException($"SymbolTable (id {objectId}) could not be opened.");
            if (table.Has(name))
                return Get<T>(table[name], OpenMode.ForWrite);
            if (f == null)
                throw new ArgumentNullException(nameof(f));
            T record = f();
            record.Name = name;
            defaults?.Invoke(record);
            EnsureCanWrite(table);
            table.Add(record);
            Transaction.AddNewlyCreatedDBObject(record, true);
            return record;
        }
    }
}