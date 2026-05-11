#pragma warning disable CS8618
using System.Collections.Generic; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using ZwSoft.ZwCAD.Geometry;
#else
using Autodesk.AutoCAD.Geometry;
#endif
#endregion

namespace BcToolsC.BCad.Commands.Models
{
    public class AcDbParcel
    {
        public AcDbParcel(string puid)
        { Puid = puid; }

        public Point2d? Point { get; set; }
        public readonly List<Point2d> Geometry 
            = new List<Point2d>();

        public string Land { get; set; }
        public string Uses { get; set; }
        public string Town { get; set; }
        public double Area { get; set; }
        public string Name { get; set; }
        public string Zone { get; set; }

        public string[] Bpej { get; set; }

        public readonly string Puid;         // Parcela Id

        public string Zuid { get; set; }     // Katastrální území Id
        public string Tuid { get; set; }     // Obec Id
        public string Buid { get; set; }     // Budova Id
    }
}