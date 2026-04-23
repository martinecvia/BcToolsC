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
using NetTopologySuite.Geometries;
using BcToolsC.BCad.Transactions;

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

        T Deserialize<T>(string json)
        {
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(ms);
            }
        }

        byte[] DownloadData(string url, double timeout = 30.0)
        {
            using (TimeoutedWebClient wc = new TimeoutedWebClient { Timeout = (int)timeout * 1000 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36";
                return wc.DownloadData(url);
            }
        }

        byte[] DownloadDataWithProgress(string url, double timeout = 30.0)
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
                        progress.Start("Stahuji data ...");
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
                                System.Windows.Forms.Application.DoEvents();
                            }
                            progress.Stop();
                            return ms.ToArray();
                        }
                    }
                }
                catch (Exception exception)
                {
                    progress.Stop();
                    return null;
                }
            }
        }

        Point3dCollection GetPolylineVertices(BCadTransaction t, Curve curve)
        {
            Point3dCollection result = new Point3dCollection();
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
            else if (curve is Polyline polyLw)
            {
                for (int i = 0; i < polyLw.NumberOfVertices; i++)
                    result.Add(polyLw.GetPoint3dAt(i));
            }
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
            else if (curve is Line line)
            {
                result.Add(line.StartPoint);
                result.Add(line.EndPoint);
            }
            return result;
        }

        double InterpolateZ(Coordinate[] arr, double px, double py)
        {
            Coordinate A = arr[0];
            Coordinate B = arr[1];
            Coordinate C = arr[2];
            double dq = (B.Y - C.Y) * (A.X - C.X) + (C.X - B.X) * (A.Y - C.Y);
            if (Math.Abs(dq) < 1E-5)
                return (A.Z + B.Z + C.Z) / 3.0;
            double wA = ((B.Y - C.Y) * (px - C.X) + (C.X - B.X) * (py - C.Y)) / dq;
            double wB = ((C.Y - A.Y) * (px - C.X) + (A.X - C.X) * (py - C.Y)) / dq;
            double wC = 1.0 - wA - wB;
            return wA * A.Z + wB * B.Z + wC * C.Z;
        }

        string DownloadString(string url, double timeout = 5.0)
        {
            using (TimeoutedWebClient wc = new TimeoutedWebClient { Timeout = (int)timeout * 1000 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36";
                return wc.DownloadString(url);
            }
        }

        bool TryUnzipData(byte[] data, string lsPath, out string __saved)
        {
            __saved = default;
            if (data == null || data.Length == 0)
                return false;
            using (var ms = new MemoryStream(data))
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (var zipEntry in archive.Entries)
                {
                    string extension = Path.GetExtension(zipEntry.Name).ToLower();
                    var zipPath = Path.Combine(lsPath, zipEntry.Name);
                    using (var entryStream = zipEntry.Open())
                    using (var filesStream = File.Create(zipPath))
                    {
                        __saved = zipPath;
                        entryStream.CopyTo(filesStream);
                    }
                }
            }
            return !string.IsNullOrEmpty(__saved);
        }

        bool CanWrite(string lsPath)
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

        string GetKeywordFromPrompt(Editor editor, string prompt,
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

        Point3d? GetPointFromPrompt(Editor editor, string prompt)
        {
            PromptPointResult evResult = editor.GetPoint(new PromptPointOptions($"\n{prompt}: "));
            if (evResult.Status != PromptStatus.OK) return null;
            return evResult.Value;
        }

        SCALE? GetScaleFromPrompt(Editor editor, string prompt, int @default = 1_000)
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

        ObjectId GetEntityFromPrompt(
            Editor editor,
            string prompt,
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

        __4326 GetWGS84FromPoint(Point3d point)
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