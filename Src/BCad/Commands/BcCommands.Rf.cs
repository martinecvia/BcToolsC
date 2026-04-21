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
        internal static double[,] Rf_TypeArray_Sk = null;
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
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Editor editor = document.Editor;
            // Chyba kompilace
            int n = Rf_TypeArray_Cz?.GetLength(0) ?? 0;
            if (Rf_TypeArray_Cz == null || n == 0) return;
            Call(t =>
            {
                // Vytvoření bodů z paměťové mapy
                Point2d[] pts = new Point2d[n];
                for (int i = 0; i < n; i++)
                    pts[i] = new Point2d(Rf_TypeArray_Cz[i, 0], Rf_TypeArray_Cz[i, 1]);
                Polyline polyline = t.AddLWPolyline(pts, color: 3, layer: "BcToolsC_Relief_CZ", shouldBeClosed: true);
                // Zoom do výkresu, zobrazující reliéf
                // Extents polyline
                Extents3d extents = polyline.GeometricExtents;
                var min = extents.MinPoint;
                var max = extents.MaxPoint;
                double w = max.X - min.X;
                double h = max.Y - min.Y;
                // Ochrana proti nulovým rozměrům
                if (w <= Tolerance.Global.EqualPoint ||
                    h <= Tolerance.Global.EqualPoint)
                    return;
                // astavení pohledu
                using (ViewTableRecord view = editor.GetCurrentView().Clone() as ViewTableRecord)
                {
                    view.CenterPoint = new Point2d((min.X + max.X) * .5, (min.Y + max.Y) * .5);
                    view.Width  = w * 1.1;
                    view.Height = h * 1.1;
                    editor.SetCurrentView(view);
                }
            });
        }
    }
}