#pragma warning disable
using System.Runtime.Serialization;

namespace BcToolsC.Models
{
    [DataContract]
    public readonly struct ANGLE
    {
        // Hodnota úhlu v radiánech
        [DataMember] public readonly double Angle;
        public static readonly ANGLE PI = new ANGLE(System.Math.PI);
        public static implicit operator ANGLE(double radians) => new ANGLE(radians);
        public static implicit operator double(ANGLE angle) => angle.Angle;
        private ANGLE(double radians) => Angle = radians;
        /// <summary>
        /// Vytvoří hodnotu type <see cref="ANGLE"/> z úhlu ve stupních
        /// </summary>
        /// <param name="degrees">Úhel ve stupních, který se má převést.</param>
        /// <returns><see cref="ANGLE"/> reprezentující úhel v radiánech.</returns>
        public static ANGLE FromDegrees(double degrees) => new ANGLE(degrees * System.Math.PI / 180.0);

        /// <summary>
        /// Vytvoří hodnotu type <see cref="ANGLE"/> z úhlu ve radiánech
        /// </summary>
        /// <param name="radians">Úhel ve radiánech, který se má převést.</param>
        /// <returns><see cref="ANGLE"/> reprezentující úhel v radiánech.</returns>
        public static ANGLE FromRadians(double radians) => new ANGLE(radians);

        /// <summary>
        /// Vytvoří hodnotu type <see cref="ANGLE"/> ze sklonu v %
        /// </summary>
        /// <param name="slope">Sklon ve stupních, který se má převést.</param>
        /// <returns><see cref="ANGLE"/> reprezentující úhel v radiánech.</returns>
        public static ANGLE FromSlope(double slope) => System.Math.Atan(slope * 10);

        public double Sin() => System.Math.Sin(Angle);
        public double Cos() => System.Math.Cos(Angle);
        public double Tan() => System.Math.Tan(Angle);
        public double ToDegrees() => Angle * 180.0 / System.Math.PI;
        public double ToSlope() => System.Math.Tan(Angle) / 10;
        public bool Equals(ANGLE other) => System.Math.Abs(Angle - other.Angle) < 1E-05;
        public override bool Equals(object obj) => obj is ANGLE angle && Equals(angle);
        public override int GetHashCode() => Angle.GetHashCode();
    }
}