using System; // Keep for .NET 4.6
using System.IO; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Text;
using System.Runtime.Serialization.Json;
using System.Net;
using System.IO.Compression;
using System.Windows;
using System.Text.RegularExpressions;

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
using static BcToolsC.Helpers.CompressHelper;
using BcToolsC.BCad.Transactions;
using BcToolsC.BCad.Commands.Models;

#if !NET45
using NetTopologySuite.Geometries;
#endif

[assembly: AcRun.CommandClass(typeof(BcToolsC.BCad.Commands.BcCommands))]
namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        const string __chars = "abcdefghijklmnopqrstuvwxyz";

        static AcDbCurve? GetCurve(ObjectId __curve,
            BCadTransaction r = null)
        {
            if (__curve.IsNull) return null;
            Func<BCadTransaction, AcDbCurve?> f = t =>
            {
                if (!t.TryGet(__curve, out Curve curve)) return null;
                Point3dCollection vertice = GetPolylineVertices(t, curve); ;
                if (vertice.Count < 2) return null;
                Point3d fst = vertice[0];
                Point3d lst = vertice[vertice.Count - 1];
                bool reallyClosing = fst.IsEqualTo(lst, Tolerance.Global);
                if (curve.Closed && !reallyClosing) vertice.Add(fst);
                return new AcDbCurve(curve.GeometricExtents, vertice,
                    curve.Closed, reallyClosing);
            };
            return r == null ? BCadTransaction.Wrap(f) : f(r);
        }

        static bool IsPointInPolygon(Point3d point, Point2dCollection polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            double minX = polygon[0].X, maxX = polygon[0].X;
            double minY = polygon[0].Y, maxY = polygon[0].Y;

            foreach (Point2d p in polygon)
            {
                minX = Math.Min(p.X, minX); maxX = Math.Max(p.X, maxX);
                minY = Math.Min(p.Y, minY); maxY = Math.Max(p.Y, maxY);
            }

            if (point.X < minX || point.X > maxX || point.Y < minY || point.Y > maxY) return false;

            // https://wrf.ecse.rpi.edu/Research/Short_Notes/pnpoly.html
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        static Point3dCollection GetPolylineVertices(BCadTransaction t, Curve curve)
        {
            var result = new Point3dCollection();
            switch (curve)
            {
                case Polyline3d poly3d:
                    var t3d = poly3d.PolyType;
                    foreach (ObjectId v3dId in poly3d) if (t.Exists(v3dId)
                        && t.TryGet(v3dId, out PolylineVertex3d vertex) && vertex != null)
                    {
                        if (t3d == Poly3dType.SimplePoly && vertex.VertexType == Vertex3dType.SimpleVertex)
                            result.Add(vertex.Position);
                        else if ((t3d == Poly3dType.CubicSplinePoly || t3d == Poly3dType.QuadSplinePoly)
                        && vertex.VertexType != Vertex3dType.ControlVertex)
                            result.Add(vertex.Position);
                    }
                    break;
                case Polyline2d poly2d:
                    var t2d = poly2d.PolyType;
                    foreach (ObjectId v2dId in poly2d) if (t.Exists(v2dId)
                        && t.TryGet(v2dId, out Vertex2d vertex) && vertex != null)
                    {
                        if (t2d == Poly2dType.SimplePoly && vertex.VertexType == Vertex2dType.SimpleVertex)
                            result.Add(vertex.Position);
                        // Další druhy 2d polyline neřešíme, protože nejsou už podporované
                    }
                    break;
                case Polyline polyLw:
                    int n = polyLw.NumberOfVertices;
                    for (int i = 0; i < n; i++)
                    {
                        var p = polyLw.GetPoint3dAt(i);
                        result.Add(p);
                        int j = (i + 1) % n;
                        if (!polyLw.Closed && i == n - 1) break;
                        double bulge = polyLw.GetBulgeAt(i);
                        if (Math.Abs(bulge) > 1E-5)
                        {
                            Point3d pB = polyLw.GetPoint3dAt(j);
                            ArcToVertices(result, BulgeToArc(p, pB, bulge));
                        }
                    }
                    break;
                case Arc arc:
                    ArcToVertices(result, arc);
                    break;
                case Circle circle:
                    ArcToVertices(result, new Arc(circle.Center, circle.Radius, 0, Math.PI * 2.0));
                    break;
                case Line line:
                    result.Add(line.StartPoint);
                    result.Add(line.EndPoint);
                    break;
                case Spline spline:
                    if (spline.HasFitData) for (int i = 0; i < spline.NumFitPoints; i++)
                        result.Add(spline.GetFitPointAt(i));
                    else for (int i = 0; i < spline.NumControlPoints; i++)
                        result.Add(spline.GetControlPointAt(i));
                    break;
                default: break;
            }
            return result;
        }

        static Arc BulgeToArc(Point3d a, Point3d b, 
            double bulge)
        {
            // Výpočet geometrie oblouku z prohnutí (bulge)
            ANGLE angle = ANGLE.FromBulge(bulge);
            double length = a.DistanceTo(b);
            double radius = length / 2.0 / Math.Sin(angle / 2.0);

            // Střed tětivy 
            var pM = a + (b - a) * 0.5;

            // Kolmice k tětivě (směr ke středu oblouku)
            var cH = b - a;
            var pE = cH.RotateBy(Math.PI / 2.0, Vector3d.ZAxis).GetNormal();
            var pS = length / 2.0 / Math.Tan(angle / 2.0);

            // Výpočet úhlů
            Point3d center = (bulge > 0) ? pM + pE * pS : pM - pE * pS;
            Vector3d vS = a - center;
            Vector3d vE = b - center;

            double startAng = Math.Atan2(vS.Y, vS.X);
            double endAng   = Math.Atan2(vE.Y, vE.X);

            // V AutoCADu musí jít Arc vždy proti směru hodin (CCW) (counter-clockwise)
            if (bulge < 0)
                return new Arc(center, radius, 
                    endAng, startAng);
            return new Arc(center, radius, 
                    startAng, endAng);
        }

        static void ArcToVertices(Point3dCollection _vertices, Arc arc)
        {
            double length = arc.Length;
            if (length < 1E-5) return;

            // Dynamický počet segmentů
            int segments = (int)Math.Max(2, length / 0.5);
            segments = Math.Max(3, Math.Min(segments, 100));
            double startAng = arc.StartAngle;
            double endAng = arc.EndAngle;
            if (endAng < startAng) endAng += Math.PI * 2.0;

            double step = (endAng - startAng) / segments;
            for (int i = 1; i < segments; i++)
            {
                ANGLE angle = startAng + (i * step);
                _vertices.Add(arc.GetPointAtParameter(angle));
            }
        }

        static double ReadDouble(string s)
        {
            // Soubory používají jak , tak . jako desetinnou čárku
            // pokud by jsme neprováděli konverzi,
            // může se stát že se bude brát pouze číslo za des. čárkou (což je blbost)
            if (string.IsNullOrWhiteSpace(s)) return 0.0;
            if (double.TryParse(s.Trim().Replace(',', '.'), out double result)) return result;
            return 0.0;
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
                    request.ServicePoint.Expect100Continue = false;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream remoteStream = response.GetResponseStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        long size = response.ContentLength;
                        progress.Start(message);
                        if (size > 0) progress.SetLimit(100);

                        byte[] buffer = new byte[8192];
                        int bytesRead, totalRead = 0, last = 0;
                        while ((bytesRead = remoteStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ms.Write(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                            if (size > 0)
                            {
                                int curr = (int)((double)totalRead / size * 100);
                                if (curr > last)
                                {
                                    for (int i = 0; i < curr - last; i++)
                                        progress.MeterProgress();
                                    last = curr;
#pragma warning disable CA1416 // Validate platform compatibility
                                    System.Windows.Forms.Application.DoEvents();
#pragma warning restore CA1416 // Validate platform compatibility
                                }
                            }
                        }
                        return ms.ToArray();
                    }
                } catch { return null; } 
                finally { progress.Stop(); }
            }
        }

        static string DownloadString(string url, double timeout = 5.0)
        {
            using (TimeoutedWebClient wc = new TimeoutedWebClient { Timeout = (int)timeout * 1000, Encoding = Encoding.UTF8 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36";
                return wc.DownloadString(url);
            }
        }

        static bool TryFetchAtomic(string theme,
            __4326 wgs84,
            out AtomicEntries response)
        {
            response = null;
            try
            {
                string url = string.Format("https://atom.cuzk.cz/get.ashx?format=json&searchTerms=&theme={0}&crs=JTSK&bbox={1},{2},{1},{2}",
                    theme, wgs84.L, wgs84.B);
                Console.WriteLine(url);
                string json = DownloadString(url);
                if (string.IsNullOrWhiteSpace(json)) throw new Exception("Prázdná odpověď serveru.");
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AtomicEntries));
                    if (serializer == null) return default;
                    response = (AtomicEntries)serializer.ReadObject(ms);
                }
            } catch { return false; }
            if (response?.Entries != null && response.Entries.Count > 0)
                return true;
            return false;
        }

        static bool TryUnzipData(byte[] data, string dir, string prefferedExtension,
            out string anyFile)
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
                    
                    // Získání reálné cesty k souboru s preferovanou přípnou
                    if (!string.IsNullOrEmpty(prefferedExtension))
                    {
                        var entry = archive.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name) &&
                            string.Equals(Path.GetExtension(e.Name), prefferedExtension, StringComparison.OrdinalIgnoreCase));
                        if (entry != null) anyFile = Path.Combine(dir, entry.Name);
                    }
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
                            entryStream.CopyTo(filesStream);
                            if (string.IsNullOrEmpty(anyFile)) anyFile = zipPath;
                        }
                    }
                }
            } catch (Exception exception)
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
            if (argv == null || argv.Length == 0) return null;
            PromptKeywordOptions options = new PromptKeywordOptions($"\n{prompt}: ") { AllowNone = false };
            for (int i = 0; i < argv.Length; i++)
            {
                if (i >= __chars.Length) break;
                // Nahrazujeme mezery dočastnou neviditelnou mezerou, aby se výběr neořezal před mezerou
                string k = __chars[i] + argv[i].Replace(" ", "\u3164");
                options.Keywords.Add(k);
            }
            PromptResult evResult = editor.GetKeywords(options);
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
            // S-JTSK pracuje v opačném kvadrantu, proto jsou tyto data prohozeny
            double x = point.Y;
            double y = point.X;
            double z = point.Z;
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

        static bool ValidateLastoolInstall(Editor editor, out string exePath)
        {
            exePath = default;
            if (!Environment.Is64BitOperatingSystem)
            {
                editor.Error("Chyba; 64-bit operační systém je vyžadován k této operaci.");
                return false;
            }
            string tempDir = Path.GetTempPath();
            if (!ValidateDirectoryWritable(editor, tempDir)) return false;
            exePath = Path.Combine(tempDir, "las2txt.exe");
            if (File.Exists(exePath)) return true;
            MessageBoxResult result = MessageBox.Show(
                "Nástroj „las2txt.exe“ není na tomto počítači nalezen.\n\n" +
                "Součást balíku LAStools:\n" +
                "https://github.com/LAStools/LAStools\n" +
                "(c) 2007–2024 rapidlasso GmbH\n\n" +
                "Přejete si jej nyní nainstalovat do dočasného adresáře?",
                "Chybí nástroj LAStools",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return false;
            }
            // Deserialize embedded EXE
            byte[] exeData;
            try
            {
                exeData = DeserializeExeFromBase64(Repository.COMPILE_LASTOOL_HASH,
                    Repository.COMPILE_LASTOOL0, Repository.COMPILE_LASTOOL1,
                    Repository.COMPILE_LASTOOL2, Repository.COMPILE_LASTOOL3,
                    Repository.COMPILE_LASTOOL4, Repository.COMPILE_LASTOOL5,
                    Repository.COMPILE_LASTOOL6, Repository.COMPILE_LASTOOL7,
                    Repository.COMPILE_LASTOOL8);
                if (exeData == null || exeData.Length == 0)
                {
                    editor.Error("Chyba; Poškození paměti: Data nástroje jsou poškozena (nesouhlasí kontrolní součet).");
                    return false; 
                }
            } catch (Exception exception)
            { editor.Error("Chyba; " + exception.Message); return false; }
            try { File.WriteAllBytes(exePath, exeData); }
            catch (Exception exception)
            { editor.Error("Chyba; " + exception.Message); }
            return File.Exists(exePath);
        }

        static bool ValidateAppVersion(Editor editor)
        {
            var limited = BcApp.IsAppLimitedByNetVersion;
            var supportedPlatform = BcApp.IsAcad ? "AutoCAD" : "ZwCAD";
            if (limited) editor.Warn($"Akce vyžaduje vyšší verzi {supportedPlatform}.");
            return !limited;
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
            point = __point.TransformBy(editor.CurrentUserCoordinateSystem);
            var min = envelope.MinPoint;
            var max = envelope.MaxPoint;
            bool inside = point.X > min.X && point.X < max.X &&
                point.Y > min.Y && point.Y < max.Y;
            if (inside) return true;
            editor.Warn("Bod leží mimo reliéf.");
            return false;
        }

        static bool InsideRelief(double x, double y,
            out double rX, out double rY)
        {
            rX = -Math.Abs(x); rY = -Math.Abs(y);
            if (rX < rY)
            {
                double tmp = rX;
                rX = rY;
                rY = tmp;
            }
            var envelope = BcApp.Envelope;
            var min = envelope.MinPoint;
            var max = envelope.MaxPoint;
            return rX > min.X && rX < max.X && rY > min.Y && rY < max.Y;
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
            if (__extents == null) return false;
            try
            {
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
                    if (w < 1E-5 || h < 1E-5) return false;

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

#if !NET45
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
#endif
    }
}