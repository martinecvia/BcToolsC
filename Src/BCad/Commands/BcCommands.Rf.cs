using System; // Keep for .NET 4.6
using System.IO;
using System.IO.Compression;

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
#endif
#endregion

using static BcToolsC.BCad.Transactions.BCadTransaction;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        internal static double[,] Rf_TypeArray_Cz = null;
        internal static double[,] DeserializeFromBase64(string base64)
        {
            byte[] data = Convert.FromBase64String(base64);
            return DecompressAndDeserialize(data);
        }

        static double[,] DecompressAndDeserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
                using (var br = new BinaryReader(gzip))
                {
                    int rows = br.ReadInt32();
                    var result = new double[rows, 2];
                    for (int i = 0; i < rows; i++)
                    {
                        result[i, 0] = br.ReadDouble(); // x
                        result[i, 1] = br.ReadDouble(); // y
                    }
                    return result;
                }
            }
        }

        [AcRun.CommandMethod("BCTOOLSC_RF_CZ")]
        public void Rf_PrintRelief_Cz()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;
            if (!db.TileMode)
            {
                editor.Warn("Povoleno pouze v modelovém prostoru.");
                return;
            }    
            int n = Rf_TypeArray_Cz?.GetLength(0) ?? 0;
            if (Rf_TypeArray_Cz == null || n == 0) return;
            Polyline polyline = Wrap(t =>
            {
                // Vytvoření bodů z paměťové mapy
                Point2d[] pts = new Point2d[n];
                for (int i = 0; i < n; i++)
                    pts[i] = new Point2d(Rf_TypeArray_Cz[i, 0], Rf_TypeArray_Cz[i, 1]);
                return t.AddLWPolyline(pts, color: 3, shouldBeClosed: true);
            });
            // Zoom do výkresu, zobrazující reliéf
            // Extents polyline
            Extents3d extents = polyline.GeometricExtents;
            // Nastavení pohledu
            using (ViewTableRecord view = editor.GetCurrentView())
            {
                if (view == null) return;
                // Transformace WCS -> DCS
                Matrix3d wcsToDcs = 
                    Matrix3d.WorldToPlane(view.ViewDirection) * 
                    Matrix3d.Displacement(view.Target.GetAsVector().Negate()) * 
                    Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target);

                Point3d minDcs = extents.MinPoint.TransformBy(wcsToDcs);
                Point3d maxDcs = extents.MaxPoint.TransformBy(wcsToDcs);
                double w = maxDcs.X - minDcs.X;
                double h = maxDcs.Y - minDcs.Y;
                if (w <= Tolerance.Global.EqualPoint ||
                h <= Tolerance.Global.EqualPoint)
                    return;
                Point2d center = new Point2d(
                    (minDcs.X + maxDcs.X) * 0.5, 
                    (minDcs.Y + maxDcs.Y) * 0.5);
                // Korekce poměru stran okna
                double viewAspect = view.Width / view.Height;
                double entityAspect = w / h;
                if (entityAspect > viewAspect)
                    h = w / viewAspect;
                else
                    w = h * viewAspect;
                // Nastavení pohledu (malá rezerva kolem entity)
                const double margin = 1.005;
                view.CenterPoint = center;
                // Necháváme zhruba 5% rezervu
                view.Width  = w * margin;
                view.Height = h * margin;
                editor.SetCurrentView(view);
            }
            editor.Ok("Ok; Vykreslen reliéf ČR");
        }
    }
}