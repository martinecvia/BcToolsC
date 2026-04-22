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

        ObjectId GetEntityFromPrompt(
            Editor editor,
            string prompt,
            params Type[] allowedTypes)
        {
            PromptEntityOptions options = new PromptEntityOptions($"\n{prompt}:") { AllowNone = false };
            options.SetRejectMessage("Not a valid entity!");
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
            __4326 epsg;
            if (z > 0)
                epsg = SJTSK_WGS84(x, y, z);
            else
                epsg = SJTSK_WGS84(x, y);
            return epsg;
        }
    }
}