#pragma warning disable
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
using System.Windows;
using System.Text.RegularExpressions;
using System.Linq;

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

        static bool TryUnzipData(byte[] data, string dir, out string anyFile)
        {
            anyFile = default;
            if (data == null || data.Length == 0)
                return false;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    bool? overwriteAll = null;
                    foreach (var zipEntry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(zipEntry.Name)) continue; // Directory
                        var zipPath = Path.Combine(dir, zipEntry.Name);
                        if (File.Exists(zipPath))
                        {
                            if (overwriteAll == null)
                            {
                                overwriteAll = GetFileOverrideAnswerFromPrompt();
                                if (overwriteAll == false) return false;
                            }
                            if (IsLocked(zipPath))
                            {
                                MessageBox.Show(
                                   "Soubor je právě používán jiným procesem nebo je zamčený pro zápis.",
                                   "Soubor nelze přepsat",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                                return false;
                            }
                        }
                        using (var entryStream = zipEntry.Open())
                        using (var filesStream = File.Create(zipPath))
                        {
                            anyFile = zipPath;
                            entryStream.CopyTo(filesStream);
                        }
                    }
                }
            }
            catch (Exception exception)
            { Console.WriteLine(exception.Message); }
            return !string.IsNullOrEmpty(anyFile);
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

        static bool CanWrite(string dir)
        {
            string lsFile = Path.Combine(dir, Path.GetRandomFileName());
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

        static bool TrySelectEntry(Editor editor, AtomicEntries atom,
            Regex regex, 
            out AtomicEntries.Entry entry)
        {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            entry = null;
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
            if (atom.Entries.Count == 1)
            {
                entry = atom.Entries[0];
                return true;
            }
            List<string> names = new List<string>();
            foreach (var e in atom.Entries)
            {
                var match = regex.Match(e.Name);
                if (match.Success) names.Add(match.Groups[1].Value.Trim());
            }
            if (names.Count == 0) return false;
            var selected = GetKeywordFromPrompt(editor, "Vyberte mapový list", names.ToArray());
            if (string.IsNullOrEmpty(selected))
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return false;
            }
#pragma warning disable CS8601 // Possible null reference assignment.
            entry = atom.Entries.FirstOrDefault(e => e.Name.Contains(selected));
#pragma warning restore CS8601 // Possible null reference assignment.
            return entry != null;
        }

        static bool TrySelectEntry(Editor editor, AtomicEntries atom,
            string splitString,
            out AtomicEntries.Entry entry)
        {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            entry = null;
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
            if (atom.Entries.Count == 1)
            {
                entry = atom.Entries[0];
                return true;
            }
            List<string> names = new List<string>();
            foreach (var e in atom.Entries)
            {
                var parts = Regex.Split(e.Name, splitString);
                if (parts.Length > 1) names.Add(parts[1]);
            }
            if (names.Count == 0) return false;
            var selected = GetKeywordFromPrompt(editor, "Vyberte mapový list", names.ToArray());
            if (string.IsNullOrEmpty(selected))
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return false;
            }
#pragma warning disable CS8601 // Possible null reference assignment.
            entry = atom.Entries.FirstOrDefault(e => e.Name.Contains(selected));
#pragma warning restore CS8601 // Possible null reference assignment.
            return entry != null;
        }

        static bool GetFileOverrideAnswerFromPrompt()
        {
            MessageBoxResult result = MessageBox.Show(
                "Soubor se už v adresáři nachází!",
                "Přepsat?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }

        static bool ValidateModelSpace(Editor editor, Database database)
        {
            if (database.TileMode)
                return true;
            editor.Warn("Povoleno pouze v modelovém prostoru.");
            return false;
        }

        static bool ValidatePointInsideRelief(Editor editor, Point3d __point, out Point3d point)
        {
            var envelope = BcApp.Envelope;
            // Transformace do správného souřadnicového systému
            Matrix3d transform = editor.CurrentUserCoordinateSystem;
            point = __point.TransformBy(transform);
            var min = envelope.MinPoint;
            var max = envelope.MaxPoint;
            bool inside = point.X > min.X && point.X < max.X &&
                point.Y > min.Y && point.Y < max.Y;
            if (inside) return true;
            editor.Warn("Bod leží mimo reliéf.");
            return false;
        }

        static bool ValidateDrawingPath(Editor editor, 
            out string lsPath)
        {
            lsPath = BcApp.CurrentDirectory;
            if (!string.IsNullOrEmpty(lsPath))
                return true;
            editor.Error("Výkres musí být před použitím příkazu uložený!");
            return false;
        }

        static bool ValidateDirectoryWritable(Editor editor, string lsPath)
        {
            if (CanWrite(lsPath))
                return true;
            editor.Warn("Adresář není zapisovatelný!");
            return false;
        }

        static bool TryZoomToExtents(Editor editor, Extents3d? __extents)
        {
            try
            {
                if (__extents == null) return false;
                var extents = __extents.Value;
                using (ViewTableRecord view = editor.GetCurrentView())
                {
                    if (view == null) return false;

                    // Transformace do device coordinate system
                    Matrix3d transform =
                        Matrix3d.WorldToPlane(view.ViewDirection) *
                        Matrix3d.Displacement(view.Target.GetAsVector().Negate()) *
                        Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target);
                    Point3d min = extents.MinPoint.TransformBy(transform);
                    Point3d max = extents.MaxPoint.TransformBy(transform);
                    double w = max.X - min.X;
                    double h = max.Y - min.Y;

                    // Zobrazované entity jsou příliš malé
                    if (w <= Tolerance.Global.EqualPoint ||
                        h <= Tolerance.Global.EqualPoint)
                        return false;

                    // Korekce poměru stran okna
                    double viewAspect = view.Width / view.Height;
                    double zoomAspect = w / h;
                    if (zoomAspect > viewAspect)
                        h = w / viewAspect;
                    else
                        w = h * viewAspect;

                    // Nastavení pohledu (malá rezerva kolem entity)
                    const double MARGIN = 1.005;
                    view.CenterPoint = new Point2d(
                        (min.X + max.X) * 0.5,
                        (min.Y + max.Y) * 0.5);

                    // Necháváme zhruba 5% rezervu
                    view.Width = w * MARGIN;
                    view.Height = h * MARGIN;
                    editor.SetCurrentView(view);
                    return true;
                }
            } catch (Exception exception)
            { editor.Error("Chyba; " + exception.Message); }
            return false;
        }

        static string ResolvePath(string lsPath, 
            string dir = null)
        {
            if (string.IsNullOrEmpty(lsPath)) return null;
            // Absolutní cesta
            if (Path.IsPathRooted(lsPath) && File.Exists(lsPath))
                return lsPath;
            // Relativní cesta - vázaná na adresář výkresu
            if (string.IsNullOrEmpty(dir)) return null;
            string rlPath = Path.GetFullPath(Path.Combine(dir, lsPath));
            if (File.Exists(rlPath))
                return rlPath;
            return null;
        }
    }
}