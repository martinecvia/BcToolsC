using System.Collections.Generic; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM 
#if ZWCAD
using ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Colors;
#else
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Colors;
#endif
#endregion

namespace BcToolsC.BCad.Transactions
{
    // https://keanw.com/2010/01/creating-an-autocad-layer-using-net.html
    // https://keanw.com/2015/08/checking-for-built-in-autocad-objects-using-net.html
    public partial class BCadTransaction
    {
        public LayerTableRecord GetCurrentLayer() => Get<LayerTableRecord>(Database.Clayer);
        public bool TryGetLayer(string name, out LayerTableRecord layer,
            OpenMode mode = OpenMode.ForRead)
        {
            layer = null;
            if (!HasRecord(name, Database.LayerTableId))
                return false;
            LayerTable table = Get<LayerTable>(Database.LayerTableId);
            return TryGet(table[name], out layer, mode);
        }

        public bool TryGetLinetype(string name, out LinetypeTableRecord linetype,
            OpenMode mode = OpenMode.ForRead)
        {
            linetype = null;
            if (!HasRecord(name, Database.LinetypeTableId))
                return false;
            LinetypeTable table = Get<LinetypeTable>(Database.LinetypeTableId);
            return TryGet(table[name], out linetype, mode);
        }

        public IEnumerable<LayerTableRecord> GetLayerList(OpenMode mode = OpenMode.ForRead)
        {
            LayerTable table = Get<LayerTable>(Database.LayerTableId);
            if (table == null) yield break;
            foreach (ObjectId objectId in table)
                if (TryGet(objectId, out LayerTableRecord layer, mode))
                    yield return layer;
        }

        public IEnumerable<LinetypeTableRecord> GetLinetypeList(OpenMode mode = OpenMode.ForRead)
        {
            LinetypeTable table = Get<LinetypeTable>(Database.LinetypeTableId);
            if (table == null) yield break;
            foreach (ObjectId objectId in table)
                if (TryGet(objectId, out LinetypeTableRecord linetype, mode))
                    yield return linetype;
        }

        public bool RemoveLayer(string name)
        {
            if (!TryGetLayer(name, out LayerTableRecord record, OpenMode.ForWrite)) return false;
            try
            {
                record.Erase();
                return true;
            }
            catch
            {
                // Pravdepodobne je záznam nastaven jako aktuální, nebo je někde použit.
                return false;
            }
        }

        public bool RemoveLinetype(string name)
        {
            if (!TryGetLinetype(name, out LinetypeTableRecord record, OpenMode.ForWrite)) return false;
            try
            {
                record.Erase();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public LayerTableRecord CreateLayer(string name,
            short colorIndex = 0, string linetype = "Continuous",
            bool frozen = false, bool locked = false, bool hidden = false)
        {
            return GetOrAdd_Layer(name, layer =>
            {
                layer.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
                layer.LinetypeObjectId = EnsureLinetype(linetype);
                layer.IsLocked = locked;
                layer.IsFrozen = frozen;
                layer.IsHidden = hidden;
            });
        }

        public void FreezeLayer(string name)
        {
            if (!TryGetLayer(name, out var layer)) return;
            EnsureCanWrite(layer);
            layer.IsFrozen = true;
        }

        public void ThawLayer(string name)
        {
            if (!TryGetLayer(name, out var layer)) return;
            EnsureCanWrite(layer);
            layer.IsFrozen = false;
        }

        public void LockLayer(string name)
        {
            if (!TryGetLayer(name, out var layer)) return;
            EnsureCanWrite(layer);
            layer.IsLocked = true;
        }

        public void UnlockLayer(string name)
        {
            if (!TryGetLayer(name, out var layer)) return;
            EnsureCanWrite(layer);
            layer.IsLocked = false;
        }

        public void HideLayer(string name)
        {
            if (!TryGetLayer(name, out var layer)) return;
            EnsureCanWrite(layer);
            layer.IsHidden = true;
        }

        public void ShowLayer(string name)
        {
            if (!TryGetLayer(name, out var layer)) return;
            EnsureCanWrite(layer);
            layer.IsHidden = false;
        }

        private ObjectId EnsureLayer(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Database.LayerZero;
            if (TryGetLayer(name, out LayerTableRecord layer, OpenMode.ForWrite))
                return layer.ObjectId;
            layer = CreateLayer(name);
            return layer.ObjectId;
        }

        private ObjectId EnsureLinetype(string name, OpenMode mode = OpenMode.ForRead,
            string linetypeFile = null, DefaultLinetype defaultLinetype = DefaultLinetype.ByLayer)
        {
            if (string.IsNullOrWhiteSpace(name))
                return EnsureDefaultLinetype(defaultLinetype);
            if (TryGetLinetype(name, out LinetypeTableRecord linetype, mode))
                return linetype.ObjectId;
            try
            {
                // https://forums.autodesk.com/t5/net-forum/error-to-create-a-dashed-line/td-p/7698463
                Database.LoadLineTypeFile(name, linetypeFile ?? (Database.Measurement == MeasurementValue.English ?
#if ZWCAD
    "zwcad.lin" : "zwcadiso.lin"
#else
    "acad.lin" : "acadiso.lin"
#endif  
                ));
                if (TryGetLinetype(name, out linetype, mode))
                    return linetype.ObjectId;
                return EnsureDefaultLinetype(defaultLinetype);
            }
            catch (Exception)
            {
                return EnsureDefaultLinetype(defaultLinetype);
            }
        }

        public enum DefaultLinetype
        {
            ByBlock,
            ByLayer,
            Default
        }

        ObjectId EnsureDefaultLinetype(DefaultLinetype defaultLinetype)
        {
            switch (defaultLinetype)
            {
                case DefaultLinetype.ByLayer:
                    return Database.ByLayerLinetype;
                case DefaultLinetype.ByBlock:
                    return Database.ByBlockLinetype;
                default:
                    return Database.ContinuousLinetype;
            }
        }
    }
}