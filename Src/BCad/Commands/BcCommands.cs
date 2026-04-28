#pragma warning disable IDE0028, IDE0063, IDE0079, IDE0090, CS8600, CS8603, CS8618
using System; // Keep for .NET 4.6
using System.IO; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Text;
using System.Runtime.Serialization.Json;
using System.Net;
using System.IO.Compression;
using System.Runtime.Serialization;

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
#else
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
#endif
#endregion

using BcToolsC.Models;
using static BcToolsC.Helpers.KrovakHelper;
using BcToolsC.BCad.Transactions;
using NetTopologySuite.Geometries;

[assembly: AcRun.CommandClass(typeof(BcToolsC.BCad.Commands.BcCommands))]
namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        const string __chars = "abcdefghijklmnopqrstuvwxyz";

        [DataContract]
        class AtomicEntries
        {
            [DataMember(Name = "entry", IsRequired = false, EmitDefaultValue = false)]
            public List<Entry> Entries { get; set; }
            [DataContract]
            public class Entry
            {
                [DataMember(Name = "id", IsRequired = true)]
                public string Link { get; set; }
                [DataMember(Name = "title", IsRequired = true)]
                public string Name { get; set; }
            }
        }

        [DataContract]
        readonly struct AcDbCurve
        {
            public readonly Extents3d Bounds;
            public readonly Point3dCollection Vertices;
            public readonly bool Closed;
            public readonly bool ReallyClosing;
            public AcDbCurve(Extents3d _bounds, Point3dCollection _vertices, bool _closed, bool _reallyClosing)
            {
                Bounds = _bounds;
                Vertices = _vertices;
                Closed = _closed;
                ReallyClosing = _reallyClosing;
            }
        }

        static AcDbCurve? GetCurve(ObjectId __curve,
            BCadTransaction r = null)
        {
            if (__curve.IsNull) return null;
            AcDbCurve? f(BCadTransaction t)
            {
                if (!t.TryGet(__curve, out Curve curve)) return null;
                Point3dCollection vertice = GetPolylineVertices(t, curve);
                if (vertice.Count < 2) return null;
                Point3d fst = vertice[0];
                Point3d lst = vertice[vertice.Count - 1];
                bool reallyClosing = fst.IsEqualTo(lst);
                if (curve.Closed && !reallyClosing) vertice.Add(fst);
                return new AcDbCurve(curve.GeometricExtents, vertice, 
                    curve.Closed, reallyClosing);
            }
            if (r == null)
                return BCadTransaction.Wrap(f);
            else
                return f(r);
        }

        static bool GetIntersectionArea(Geometry polygon, Geometry ring,
            out Geometry intersection, out double area)
        {
            intersection = null;
            area = default;
            if (ring.Covers(polygon)) return true;
            var inside = ring.Intersects(polygon);
            if (!inside) return false;
            intersection = polygon.Intersection(ring);
            if (intersection == null || intersection.IsEmpty) return false;
            Console.WriteLine(intersection.Area);
            return inside;
        }

        static Point3dCollection GetPolylineVertices(BCadTransaction t, Curve curve)
        {
            Point3dCollection result = new Point3dCollection();
            // Polyline3d
            if (curve is Polyline3d poly3d)
            {
                var type = poly3d.PolyType;
                foreach (ObjectId v3dId in poly3d) if (t.Exists(v3dId)
                    && t.TryGet(v3dId, out PolylineVertex3d vertex) && vertex != null)
                {
                    if (type == Poly3dType.SimplePoly && vertex.VertexType == Vertex3dType.SimpleVertex)
                        result.Add(vertex.Position);
                    else if ((type == Poly3dType.CubicSplinePoly || type == Poly3dType.QuadSplinePoly)
                    && vertex.VertexType != Vertex3dType.ControlVertex)
                        result.Add(vertex.Position);
                }
            }
            // Polyline
            else if (curve is Polyline polyLw)
            {
                for (int i = 0; i < polyLw.NumberOfVertices; i++)
                    result.Add(polyLw.GetPoint3dAt(i));
            }
            // Legacy polyline, ale může se objevit ještě v některých starších výkresech
            else if (curve is Polyline2d poly2d)
            {
                var type = poly2d.PolyType;
                foreach (ObjectId v2dId in poly2d) if (t.Exists(v2dId)
                    && t.TryGet(v2dId, out Vertex2d vertex) && vertex != null)
                {
                    if (type == Poly2dType.SimplePoly && vertex.VertexType == Vertex2dType.SimpleVertex)
                        result.Add(vertex.Position);
                    // Další druhy 2d polyline neřešíme
                }
            }
            // Spline
            else if (curve is Spline spline)
            {
                if (spline.HasFitData)
                    for (int i = 0; i < spline.NumFitPoints; i++)
                        result.Add(spline.GetFitPointAt(i));
                else
                    for (int i = 0; i < spline.NumControlPoints; i++)
                        result.Add(spline.GetControlPointAt(i));
            }
            // Line
            else if (curve is Line line)
            {
                result.Add(line.StartPoint);
                result.Add(line.EndPoint);
            }
            return result;
        }

        static T Deserialize<T>(string json)
        {
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                if (serializer == null) return default;
                return (T)serializer.ReadObject(ms);
            }
        }

        byte[] DownloadDataWithProgress(string url, 
            double timeout = 30.0, string message = "Stahuji data ...")
        {
            using (AcRun.ProgressMeter progress = new AcRun.ProgressMeter())
            {
                try
                {
#pragma warning disable SYSLIB0014 // Type or member is obsolete
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014 // Type or member is obsolete
                    request.Timeout = (int)(timeout * 1_000);
                    request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36";
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        long size = response.ContentLength;
                        bool hasSize = size > 0;
                        progress.Start(message);
                        if (hasSize) progress.SetLimit(100);
                        using (Stream remoteStream = response.GetResponseStream())
                        using (MemoryStream ms = new MemoryStream())
                        {
                            byte[] buffer = new byte[8192];
                            var totalRead = 0;
                            var bytesRead = 0;
                            var last = 0;
                            while ((bytesRead = remoteStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                ms.Write(buffer, 0, bytesRead);
                                totalRead += bytesRead;
                                if (hasSize)
                                {
                                    int curr = (int)((double)totalRead / size * 100);
                                    if (curr > last)
                                    {
                                        for (int i = 0; i < curr - last; i++)
                                            progress.MeterProgress();
                                        last = curr;
                                    }
                                }
#pragma warning disable CA1416 // Validate platform compatibility
                                System.Windows.Forms.Application.DoEvents();
#pragma warning restore CA1416 // Validate platform compatibility
                            }
                            progress.Stop();
                            return ms.ToArray();
                        }
                    }
                }
                catch (Exception)
                {
                    progress.Stop();
                    return null;
                }
            }
        }

        static string DownloadString(string url, double timeout = 5.0)
        {
            using (TimeoutedWebClient wc = new TimeoutedWebClient { Timeout = (int)timeout * 1000 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36";
                return wc.DownloadString(url);
            }
        }

        static bool TryUnzipData(byte[] data, string lsPath, out string __saved)
        {
            __saved = default;
            if (data == null || data.Length == 0)
                return false;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    foreach (var zipEntry in archive.Entries)
                    {
                        var zipPath = Path.Combine(lsPath, zipEntry.Name);
                        using (var entryStream = zipEntry.Open())
                        using (var filesStream = File.Create(zipPath))
                        {
                            __saved = zipPath;
                            entryStream.CopyTo(filesStream);
                        }
                    }
                }
            }
            catch (Exception exception)
            { Console.WriteLine(exception.Message); }
            return !string.IsNullOrEmpty(__saved);
        }

        static bool IsLocked(string lsFile)
        {
            try
            {
                using (FileStream stream = new FileStream(lsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    return false;
            }
            catch (PathTooLongException) { }
            catch (DirectoryNotFoundException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (Exception) { }
            return true;
        }

        static bool CanWrite(string lsPath)
        {
            string lsFile = Path.Combine(lsPath, Path.GetRandomFileName());
            try
            {
                using (FileStream stream = File.Create(lsFile, 1, FileOptions.DeleteOnClose))
                    return true;
            }
            catch (PathTooLongException) { }
            catch (DirectoryNotFoundException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (Exception) { }
            return false;
        }

        static string GetKeywordFromPrompt(Editor editor, string prompt,
            params string[] argv)
        {
            if (argv.Length == 0) return null;
            PromptKeywordOptions format = new PromptKeywordOptions($"\n{prompt}: ") { AllowNone = false, };
            for (int i = 0; i < argv.Length; i++)
            {
                if (i > __chars.Length) break;
                string k = __chars[i] + argv[i];
                // Nahrazujeme mezery dočastnou neviditelnou mezerou, aby se výběr neořezal před mezerou
                format.Keywords.Add(k.Replace(" ", "\u3164"));
            }
            PromptResult evResult = editor.GetKeywords(format);
            if (evResult.Status != PromptStatus.OK) return null;
            return evResult.StringResult.Substring(1).Replace("\u3164", " ");
        }

        static Point3d? GetPointFromPrompt(Editor editor, string prompt)
        {
            PromptPointResult evResult = editor.GetPoint(new PromptPointOptions($"\n{prompt}: "));
            if (evResult.Status != PromptStatus.OK) return null;
            return evResult.Value;
        }

        static SCALE? GetScaleFromPrompt(Editor editor, string prompt, 
            int @default = 1_000)
        {
            PromptIntegerOptions options = new PromptIntegerOptions($"\n{prompt}: ")
            {
                DefaultValue = @default,
                LowerLimit = 1,
                UpperLimit = 1_000,
                AllowNegative = false,
                AllowNone = true
            };
            PromptIntegerResult evResult = editor.GetInteger(options);
            if (evResult.Status != PromptStatus.OK) return null;
            return new SCALE(1_000, evResult.Value);
        }

        static ObjectId GetEntityFromPrompt(Editor editor, string prompt,
            params Type[] allowedTypes)
        {
            PromptEntityOptions options = new PromptEntityOptions($"\n{prompt}:") { AllowNone = false };
            // SetRejectMessage musí být před definicí entit
            options.SetRejectMessage("Výběr není platný.");
            foreach (Type type in allowedTypes)
                options.AddAllowedClass(type, true);
            PromptEntityResult evResult = editor.GetEntity(options);
            if (evResult.Status != PromptStatus.OK) return ObjectId.Null;
            return evResult.ObjectId;
        }

        static __4326 GetWGS84FromPoint(Point3d point)
        {
            // S-JTSK pracuje v opačném kvadrantu, proto jsou tyto data prohozeny.
            double x = point.Y;
            double y = point.X;
            double z = point.Z;
            Console.WriteLine($"JTSK_X: {x}, JTSK_Y: {y}");
            __4326 epsg;
            if (z > 0)
                epsg = SJTSK_WGS84(x, y, z);
            else
                epsg = SJTSK_WGS84(x, y);
            return epsg;
        }
    }
}