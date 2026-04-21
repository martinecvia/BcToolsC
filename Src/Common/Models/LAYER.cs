using System;// Keep for .NET 4.6
using System.Runtime.Serialization;

#if CAD_PLATFORM 
#region O_PROGRAM_DETERMINE_CAD_PLATFORM 
#if ZWCAD
using ZwSoft.ZwCAD.DatabaseServices;
#else
using Autodesk.AutoCAD.DatabaseServices;
#endif
#endregion
#endif

namespace BcToolsC.Models
{
    [DataContract]
    public struct LAYER :
        IEquatable<LAYER>
    {

        [DataMember] public readonly string Name;

        [DataMember] public COLOR? Color { get; }
        [DataMember] public string Linetype { get; }
        [DataMember] public double? Lineweight { get; }

        public LAYER(string _name,
            COLOR? color = null, string _linetype = "Continuous", double? _lineweight = null)
        {
            if (string.IsNullOrEmpty(_name))
                throw new ArgumentException("Layer name cannot be null or empty", nameof(_name));
            Name = _name;
            Color = color ?? 0;
            if (string.IsNullOrEmpty(_linetype))
                _linetype = "Continuous";
            Linetype = _linetype;
            Lineweight = _lineweight;
        }

        public override bool Equals(object o) => o is LAYER other && Equals(other);
        public static bool operator ==(LAYER left, LAYER right) => left.Equals(right);
        public static bool operator !=(LAYER left, LAYER right) => !(left == right);
#if CAD_PLATFORM
        public static implicit operator LAYER(LayerTableRecord record) => new LAYER(record.Name);
#endif
        public static implicit operator LAYER(string _name) => new LAYER(_name);
        public static implicit operator string(LAYER layer) => layer.Name;
        public override string ToString() =>
            $"{Name} [Color={Color}, Linetype={Linetype}, Lineweight={Lineweight}]";
        public override int GetHashCode()
        {
            unchecked
            {
                int h = StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
                h = (h * 397) ^ Color ?? (short)0;
                h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Linetype);
                h = (h * 397) ^ (Lineweight?.GetHashCode() ?? 0);
                return h;
            }
        }
        public bool Equals(LAYER other) =>
            string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && Color == other.Color
            && string.Equals(Linetype, other.Linetype, StringComparison.OrdinalIgnoreCase)
            && Nullable.Equals(Lineweight, other.Lineweight);
    }
}