using System.Runtime.Serialization;

namespace BcToolsC.Models
{
    [DataContract]
    public readonly struct SCALE
    {
        [DataMember] public readonly double X;
        public readonly double sX;
        [DataMember] public readonly double Y;
        public readonly double sY;
        public readonly double sA;
        public SCALE(double _x = 1_000.0, double _y = 1_000.0)
        {
            if (_x < 0.0)
                _x = 1_000.0;
            X = _x;
            sX = 1_000 / _x;
            if (_y < 0.0)
                _y = 1_000.0;
            Y = _y;
            sY = 1_000 / _y;
            sA = sY / sX;
        }
    }
}