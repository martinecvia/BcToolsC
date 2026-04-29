using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM 
#if ZWCAD
using ZwSoft.ZwCAD.DatabaseServices;
using AcDb = ZwSoft.ZwCAD.DatabaseServices;
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
#else
using Autodesk.AutoCAD.DatabaseServices;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices;
#endif
#endregion

namespace BcToolsC.BCad.Transactions
{
    // https://keanw.com/2009/01/nesting-instincts-getting-more-out-of-transactions-inside-autocad-using-net.html
    public partial class BCadTransaction : IDisposable
    {
        [ThreadStatic] private static BCadTransaction _current;

        public static BCadTransaction Current => _current;

        public AcDb.Database Database { get; set; }
        public AcApp.Document Document { get; set; }
        public Transaction Transaction { get; set; }

        public BlockTable BlockTable { get; private set; }
        public BlockTableRecord ModelSpace { get; private set; }
        public AcApp.DocumentLock DocumentLock { get; private set; }

        public bool Annotative { get; set; } = false;

        bool _commited;
        public bool Commited => _commited;
        bool _isrooted;
        public bool IsRooted => _isrooted;

        public static BCadTransaction Make(AcApp.Document document = null)
        {
            AcApp.Document d = (document ?? AcApp.Core.Application.DocumentManager.MdiActiveDocument)
                ?? throw new InvalidOperationException("No active document.");
            BCadTransaction t = new BCadTransaction();
            t.BeginInit(d);
            return t;
        }

        public static void Call(Action<BCadTransaction> f)
        {
            using (BCadTransaction t = Make())
            {
                f(t);
                t.Commit();
            }
        }

        public static T Wrap<T>(Func<BCadTransaction, T> f)
        {
            using (BCadTransaction t = Make())
            {
                T result = f(t);
                t.Commit();
                return result;
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_commited)
                    Transaction?.Abort();
            }
            finally
            {
                Transaction?.Dispose();
                Transaction = null;
                if (_isrooted)
                {
                    DocumentLock?.Dispose();
                    DocumentLock = null;
                }
                if (_current == this)
                    _current = null;
            }
        }

        public void Commit()
        {
            if (!_commited && Transaction != null && !Transaction.IsDisposed)
            {
                Transaction.Commit();
                _commited = true;
            }
        }

        public void Rollback()
        {
            if (Transaction != null && !Transaction.IsDisposed)
                Transaction.Abort();
        }

        public void MoveToTop<T>(T entity)
            where T : Entity
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (!Exists(entity.ObjectId)) return;
            BlockTableRecord record = GetRequired<BlockTableRecord>(entity.OwnerId, OpenMode.ForWrite);
            DrawOrderTable drawTable = GetRequired<DrawOrderTable>(record.DrawOrderTableId, OpenMode.ForWrite);
            drawTable.MoveToTop(new ObjectIdCollection { entity.ObjectId });
        }

        public void MoveToBottom<T>(T entity)
            where T : Entity
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (!Exists(entity.ObjectId)) return;
            BlockTableRecord record = GetRequired<BlockTableRecord>(entity.OwnerId, OpenMode.ForWrite);
            DrawOrderTable drawTable = GetRequired<DrawOrderTable>(record.DrawOrderTableId, OpenMode.ForWrite);
            drawTable.MoveToBottom(new ObjectIdCollection { entity.ObjectId });
        }

        // -------------------------------------------------------------------- 
        // Core 
        // --------------------------------------------------------------------
        public T Get<T>(ObjectId objectId,
            OpenMode mode = OpenMode.ForRead)
            where T : DBObject
        {
            if (Transaction?.IsDisposed != false) return default;
            if (!Exists(objectId)) return default;
            return Transaction.GetObject(objectId, mode) as T;
        }

        // Stejné jako Get<T>(...), ale tady vždy očekáváme výstup - tedy, pokud selže, aplikace musí padnout.
        protected T GetRequired<T>(ObjectId objectId,
            OpenMode mode = OpenMode.ForRead)
            where T : DBObject
        {
            return Get<T>(objectId, mode)
                ?? throw new InvalidOperationException(
                    $"Unable to get object {objectId} as {typeof(T).Name}");
        }

        public bool TryGet<T>(ObjectId objectId, out T t,
            OpenMode mode = OpenMode.ForRead, bool throwOnException = false)
            where T : DBObject
        {
            t = null;
            try
            {
                t = Get<T>(objectId, mode);
                return t != null;
            }
            catch
            {
                if (throwOnException)
                    throw;
                return false;
            }
        }

        public void EraseEntity(ObjectId objectId)
        {
            if (!TryGet<Entity>(objectId, out var entity, OpenMode.ForWrite)) return;
            entity.Erase();
        }

        public T AddToModelSpace<T>(T entity)
            where T : Entity
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            EnsureCanWrite(ModelSpace);
            ModelSpace.AppendEntity(entity);
            Transaction.AddNewlyCreatedDBObject(entity, true);
            return entity;
        }

        protected IEnumerable<T> AddEntities<T>(IEnumerable<T> entityList)
            where T : Entity
        {
            foreach (T e in entityList)
                AddToModelSpace(e);
            return entityList;
        }

        protected T CloneEntity<T>(T entity)
            where T : Entity
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            return entity.Clone() as T;
        }

        protected T GetDictItem<T>(ObjectId objectId, string name,
            OpenMode mode = OpenMode.ForRead)
            where T : DBObject
        {
            if (!TryGet(objectId, out DBDictionary dictionary))
                return default;
            return dictionary.Contains(name) ? Get<T>(dictionary.GetAt(name), mode) : default;
        }

        protected IEnumerable<T> GetDictItemList<T>(ObjectId objectId,
            OpenMode mode = OpenMode.ForRead)
            where T : DBObject
        {
            if (!TryGet(objectId, out DBDictionary dictionary))
                yield break;
            foreach (DBDictionaryEntry entry in dictionary)
            {
                T item = Get<T>(entry.Value, mode);
                if (item != null)
                    yield return item;
            }
        }

        public void EnsureCanWrite(DBObject dBObject)
        {
            if (dBObject != null && !dBObject.IsWriteEnabled)
                dBObject.UpgradeOpen();
        }

        public void EnsureReadOnly(DBObject dBObject)
        {
            if (dBObject != null && dBObject.IsWriteEnabled)
                dBObject.DowngradeOpen();
        }

        public bool Exists(ObjectId objectId) =>
            objectId != ObjectId.Null && objectId.IsValid && !objectId.IsErased;

        void BeginInit(AcApp.Document d)
        {
            Document = d;
            Database = d.Database;
            if (_current == null)
            {
                DocumentLock = d.LockDocument();
                _isrooted = true;
            }
            _current = this;
            Transaction = Database.TransactionManager.StartTransaction();
            BlockTable = Transaction.GetObject(Database.BlockTableId, OpenMode.ForRead) as BlockTable;
            EnsureCanWrite(BlockTable);
            ModelSpace = Transaction.GetObject(BlockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForWrite) as BlockTableRecord;
        }
    }
}