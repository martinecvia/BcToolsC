#pragma warning disable
using System; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM 
#if ZWCAD
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcDb = ZwSoft.ZwCAD.DatabaseServices;
#else
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
#endif
#endregion

namespace BcToolsC.BCad
{
    public static class EditorExtensions
    {
        public static void Info(this Editor editor, object message)
            => editor.Format(message, "INFO: ");
        public static void Warn(this Editor editor, object message)
            => editor.Format(message, "WARN: ");

        public static void Error(this Editor editor, object message)
            => editor.Format(message, "ERROR: ");
        public static void Debug(this Editor editor, object message)
            => editor.Format(message, "DEBUG: ");

        public static void Ok(this Editor editor, object message)
            => editor.Format(message, "OK: ");

        public static void Log(this Editor editor, object message)
            => editor.Format(message, string.Empty);

        #region PRIVATE
        private static void Format(this Editor editor, object message, string prefix)
        {
            string tMsg = message?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(tMsg))
                tMsg = "Prázdny objekt zásobníku.";
            string formatted = string.Format("{0}{1}", prefix, tMsg.Trim());
            Console.WriteLine(formatted); // Pokud Editor nám neexistuje nebo, chceme vidìt kontinuitu programu
            editor?.WriteMessage($"\n{formatted}");
        }
        #endregion
    }

    public static class DocumentExtensions
    {
        public static void Info(this AcApp.Document document, object message)
            => document?.Editor.Info(message);
        public static void Warn(this AcApp.Document document, object message)
            => document?.Editor.Warn(message);

        public static void Error(this AcApp.Document document, object message)
            => document?.Editor.Error(message);
        public static void Debug(this AcApp.Document document, object message)
            => document?.Editor.Debug(message);

        public static void Ok(this AcApp.Document document, object message)
            => document?.Editor.Ok(message);

        public static void Log(this AcApp.Document document, object message)
            => document?.Editor.Log(message);
    }

    public static class Point2dCollectionExtension
    {
        static Point2d[] ToArray(this Point2dCollection pts)
        {
            var result = new Point2d[pts.Count];
            pts.CopyTo(result, 0);
            return result;
        }
    }

    public static class Point3dCollectionExtension
    {
        static Point3d[] ToArray(this Point3dCollection pts)
        {
            var result = new Point3d[pts.Count];
            pts.CopyTo(result, 0);
            return result;
        }
    }

    public static class BlockTableExtensions
    {
        /// <summary>
        /// Gets the objectId of a block definition (BlockTableRecord) from its key.
        /// If the block is not found in the block table, a dwg file is searched in the support paths and added to the block table.
        /// </summary>
        /// <param name="blockTable">Block table.</param>
        /// <param name="key">Block key.</param>
        /// <returns>The ObjectId of the block table record or ObjectId.Null if not found.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown if <paramref name ="blockTable"/> is null.</exception>
        /// <exception cref="System.ArgumentException">Thrown if <paramref name ="key"/> is null or empty.</exception>
        public static AcDb.ObjectId GetBlock(this AcDb.BlockTable blockTable, string key)
        {
            if (blockTable == null)
                throw new ArgumentNullException(nameof(blockTable));
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException(nameof(key));
            if (blockTable.Has(key))
                return blockTable[key];
            try
            {
                string lsPath = AcDb.HostApplicationServices.Current.FindFile(key + ".dwg", blockTable.Database, AcDb.FindFileHint.Default);
                using (AcDb.Database database = new AcDb.Database(false, true))
                {
                    database.ReadDwgFile(lsPath, AcDb.FileOpenMode.OpenForReadAndAllShare, true, null);
                    return blockTable.Database.Insert(key, database, true);
                }
            }
            catch
            {
                return AcDb.ObjectId.Null;
            }
        }
    }
}