#pragma warning disable
using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;

namespace BcToolsC.Models
{
    public class AcDbParcel
    {
        private string _zuid;
        private string _tuid;
        private string _buid;

        public AcDbParcel(string puid)
        { Puid = NormalizeId(puid); }

        public double[] Point { get; set; }
        public double[,] Geometry { get; set; }
        public string Land { get; set; }
        public string Uses { get; set; }
        public string Town { get; set; }
        public double Area { get; set; }
        public string Name { get; set; }
        public string Zone { get; set; }
        public readonly List<BonitedPart> Bpej = new List<BonitedPart>();
        public readonly string Puid;         // Parcela Id
        // Katastrální území Id
        public string Zuid
        {
            get => _zuid;
            set => _zuid = NormalizeId(value);
        }
        // Obec Id
        public string Tuid
        {
            get => _tuid;
            set => _tuid = NormalizeId(value);
        }
        // Budova Id
        public string Buid
        {
            get => _buid;
            set => _buid = NormalizeId(value);
        }

        public struct BonitedPart
        {
            public string Bpid { get; set; }
            public string Prot { get; set; }
            public double Area { get; set; }
            public override string ToString() => $"Bpej(Id={Bpid}: {Area}m2)";
        }

        public override string ToString()
        {
            string bpej = Bpej.Count == 0
                    ? string.Empty
                    : string.Join(",", Bpej);
            if (Geometry == null) Console.WriteLine("GEOMETRY: null");
            return
                $"AcDbParcel({Puid};" +
                $"{Name};" +
                $"Area={Area};" +
                $"{Land};" +
                $"{Uses};" +
                $"{Zone};" +
                $"{Tuid};" +
                $"{Zuid};" +
                $"Geometry={Geometry?.Length};" +
                $"Bpej=[{bpej}])";
        }

        public static List<AcDbParcel> ListParcelZip(byte[] data)
        {
            var result = new List<AcDbParcel>();
            if (data == null || data.Length == 0) return result;
            try
            {
                using (var ms = new MemoryStream(data, writable: false))
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false))
                {
                    var zipEntry = archive.Entries?.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name) &&
                         e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
                    if (zipEntry == null) return result;
                    using (var stream = zipEntry.Open())
                    using (var reader = XmlReader.Create(stream, new XmlReaderSettings
                    {
                        IgnoreComments = true,
                        IgnoreWhitespace = true,
                    }))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType != XmlNodeType.Element) continue;
                            switch (reader.LocalName)
                            {
                                case "Parcela":
                                case "CadastralParcel":
                                    // GML namespace URI: http://www.opengis.net/gml/3.2
                                    string id = reader.GetAttribute("gml:id");
                                    var parcel = new AcDbParcel(id);
                                    var record = ReadRecord(reader, parcel);
                                    if (parcel.Area != 0)
                                        result.Add(record);
                                    break;
                            }
                        }
                    }
                }
            } catch (Exception exception)
            { Console.WriteLine(exception.Message); }
            return result;
        }

        static Regex _tnRegex = new Regex(@"[?&]Id=([^&]+)", RegexOptions.Compiled);
        static AcDbParcel ReadRecord(XmlReader xmlReader, AcDbParcel parcel)
        {
            const GmlGeometryContext kMask = GmlGeometryContext.referencePoint | GmlGeometryContext.Point;
            const GmlGeometryContext lMask = GmlGeometryContext.Polygon
                | GmlGeometryContext.exterior
                | GmlGeometryContext.LinearRing;
            GmlGeometryContext mMask = GmlGeometryContext.None;
            using (XmlReader reader = xmlReader.ReadSubtree())
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        switch (reader.LocalName)
                        {
                            case "Polygon": mMask &= ~GmlGeometryContext.Polygon; break;
                            case "exterior": mMask &= ~GmlGeometryContext.exterior; break;
                            case "LinearRing": mMask &= ~GmlGeometryContext.LinearRing; break;
                            case "referencePoint":
                            case "DefinicniBod":
                                mMask &= ~GmlGeometryContext.referencePoint; break;
                            case "Point": mMask &= ~GmlGeometryContext.Point; break;
                        }
                        continue;
                    }
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    switch (reader.LocalName)
                    {
                        // Point
                        case "DefinicniBod":
                        case "referencePoint":
                            mMask |= GmlGeometryContext.referencePoint; break;
                        case "Point":
                            if (mMask.HasFlag(GmlGeometryContext.referencePoint))
                                mMask |= GmlGeometryContext.Point;
                            break;
                        case "pos":
                            if ((mMask & kMask) != kMask)
                            {
                                reader.Skip();
                                continue;
                            }
                            string pos = reader.ReadElementContentAsString();
                            if (string.IsNullOrEmpty(pos)) break;
                            string[] posEntries = pos.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                            if (posEntries.Length != 2) break;
                            parcel.Point = new double[] {
                                double.Parse(posEntries[0], CultureInfo.InvariantCulture),
                                double.Parse(posEntries[1], CultureInfo.InvariantCulture) };
                            break;
                        // Geometry
                        case "Polygon":
                            mMask |= GmlGeometryContext.Polygon;
                            break;
                        case "exterior":
                            if (mMask.HasFlag(GmlGeometryContext.Polygon))
                                mMask |= GmlGeometryContext.exterior;
                            break;
                        case "LinearRing":
                            if (mMask.HasFlag(GmlGeometryContext.exterior))
                                mMask |= GmlGeometryContext.LinearRing;
                            break;
                        case "posList":
                            if ((mMask & lMask) != lMask)
                            {
                                reader.Skip();
                                continue;
                            }
                            string posList = reader.ReadElementContentAsString();
                            if (string.IsNullOrEmpty(posList)) break;
                            string[] posListEntries = posList.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                            // Validace jestli má parcela dostatek bodů pro vytvoření uzavřeného polygonu
                            int n = posListEntries.Length;
                            if (n % 2 != 0)
                            {
                                Console.WriteLine($"Debug; Parcela nemá sudý počet souřadnic.");
                                continue;
                            }
                            var geometry = new double[posListEntries.Length / 2, 2];
                            var j = 0;
                            for (int i = 0; i + 1 < posListEntries.Length; i += 2)
                            {
                                geometry[j, 0] = double.Parse(posListEntries[i]    , CultureInfo.InvariantCulture);
                                geometry[j, 1] = double.Parse(posListEntries[i + 1], CultureInfo.InvariantCulture);
                                j++;
                            }
                            parcel.Geometry = geometry;
                            break;
                        // Land
                        case "DruhPozemkuKod":
                            if (reader.Read())
                            {
                                var type = reader.Value;
                                parcel.Land = MapOrDefault(Repository.LandTypeMap, type, "ostatní plocha");
                                if (type == "13")
                                    parcel.Name = "st. " + parcel.Name;
                            }
                            break;
                        case "landType":
                            parcel.Land = MapOrDefault(Repository.LandTypeMap, reader.GetAttribute("xlink:href"), "ostatní plocha");
                            break;
                        // Uses
                        case "ZpusobyVyuzitiPozemku":
                            if (reader.Read())
                                parcel.Uses = MapOrDefault(Repository.LandUsesMap, reader.Value);
                            break;
                        case "landUse":
                            parcel.Uses = MapOrDefault(Repository.LandUsesMap, reader.GetAttribute("xlink:href"));
                            break;
                        // Town
                        // Tuid
                        case "administrativeUnit":
                            parcel.Town = reader.GetAttribute("xlink:title");
                            var administrativeUnit = reader.GetAttribute("xlink:href");
                            if (string.IsNullOrEmpty(administrativeUnit)) break;
                            var m0 = _tnRegex.Match(administrativeUnit);
                            if (m0.Success) parcel.Tuid = m0.Groups[1].Value;
                            break;
                        // Area
                        case "areaValue":
                            parcel.Area = reader.ReadElementContentAsDouble();
                            break;
                        case "VymeraParcely":
                            if (reader.Read())
                                parcel.Area = double.Parse(reader.Value);
                            break;
                        // Name
                        case "label":
                            parcel.Name = reader.ReadElementContentAsString();
                            break;
                        case "KmenoveCislo":
                            if (reader.Read())
                                parcel.Name = reader.Value;
                            break;
                        case "PododdeleniCisla":
                            if (reader.Read())
                                parcel.Name += "/" + reader.Value;
                            break;
                        // Zone
                        case "zoning":
                            parcel.Zone = reader.GetAttribute("xlink:title");
                            var zoning = reader.GetAttribute("xlink:href");
                            if (string.IsNullOrEmpty(zoning)) break;
                            var m1 = _tnRegex.Match(zoning);
                            if (m1.Success) parcel.Zuid = m1.Groups[1].Value;
                            break;
                        // Buid
                        case "building":
                            parcel.Buid = reader.GetAttribute("xlink:title");
                            break;
                        case "BonitovaneDily":
                            ReadBpejRecord(reader, parcel);
                            break;
                    }
                }
            }
            return parcel;
        }

        static void ReadBpejRecord(XmlReader xmlReader, AcDbParcel parcel)
        {
            using (XmlReader reader = xmlReader.ReadSubtree())
            {
                BonitedPart bpej = new BonitedPart();
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        switch (reader.LocalName)
                        {
                            case "Vymera":
                                if (reader.Read())
                                {
                                    string area = reader.Value;
                                    if (string.IsNullOrEmpty(area)) break;
                                    bpej.Area = double.Parse(area);
                                }
                                break;
                            case "BonitovanaJednotkaKod":
                                if (reader.Read())
                                {
                                    string bpid = reader.Value;
                                    if (string.IsNullOrEmpty(bpid)) break;
                                    if (Repository.BpejPermMap.TryGetValue(bpid, out string prot))
                                    {
                                        bpej.Bpid = bpid;
                                        bpej.Prot = prot;
                                    }
                                }
                                break;
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement
                        && reader.LocalName == "BonitovanyDil")
                    {
                        if (bpej.Prot != null) parcel.Bpej.Add(bpej);
                        bpej = new BonitedPart();
                    }
                }
            }
        }

        static string MapOrDefault(
            Dictionary<string, string> map,
            string key,
            string defaultValue = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                return defaultValue;
            return map.TryGetValue(key, out var value)
                    ? value
                    : defaultValue;
        }

        static string NormalizeId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return id;
            id = id.Trim();
            int dotIndex = id.IndexOf('.');
            if (dotIndex >= 0 && dotIndex + 1 < id.Length)
                return id.Substring(dotIndex + 1).Trim();
            return id;
        }
    }    
}