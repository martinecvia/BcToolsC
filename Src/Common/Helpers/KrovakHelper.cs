#pragma warning disable
using System; // Keep for .NET 4.6

namespace BcToolsC.Helpers
{
    public static class KrovakHelper
    {
        public readonly struct WGS84 : IEquatable<WGS84>
        {
            public double Lon { get; }
            public double Lat { get; }

            public WGS84(double lon, double lat)
            {
                Lon = lon;
                Lat = lat;
            }

            public void Deconstruct(out double lon, out double lat)
            {
                lon = Lon;
                lat = Lat;
            }

            public bool Equals(WGS84 other) =>
                Lon.Equals(other.Lon) && Lat.Equals(other.Lat);

            public override bool Equals(object obj) =>
                obj is WGS84 other && Equals(other);

            public override int GetHashCode() =>
                Lon.GetHashCode() ^ Lat.GetHashCode();

            public static bool operator ==(WGS84 left, WGS84 right) => left.Equals(right);
            public static bool operator !=(WGS84 left, WGS84 right) => !left.Equals(right);

            public override string ToString() => $"Lon={Lon}, Lat={Lat}";
        }

        public static 
        WGS84
        JTSK2WGS84(double X, double Y, 
        double Height = 267)
        {
            Assert.IsNotNull(X, nameof(X));
            Assert.IsNotNull(Y, nameof(Y));
            Assert.IsNotNull(Height, nameof(Height));

            var X_JTSK = Math.Abs(Y); // Always, rotated because of rotation of XY
            var Y_JTSK = Math.Abs(X); // Always, rotated because of rotation of XY
            double ro = Math.Sqrt(X_JTSK * X_JTSK + Y_JTSK * Y_JTSK);
            double epislon = 2 * Math.Atan2(Y_JTSK, ro + X_JTSK);
            double Upper = epislon / 0.97992470462083;
            double Lower = 2 * Math.Atan(Math.Exp(1 / 0.97992470462083 * Math.Log(12310230.12797036 / ro))) - Math.PI / 2;
            double cos_Lower = Math.Cos(Lower);
            double sin_Upper = 0.863499969506341 * Math.Sin(Lower) - 0.504348889819882 * cos_Lower * Math.Cos(Upper);
            double cos_Upper = Math.Sqrt(1 - sin_Upper * sin_Upper);
            double sin_DownVertical = Math.Sin(Upper) * cos_Lower / cos_Upper;
            double cos_DownVertical = Math.Sqrt(1 - sin_DownVertical * sin_DownVertical);
            var tan = Math.Exp(2 / 1.000597498371542 * Math.Log((1 + sin_Upper) / cos_Upper / 1.003419163966575));
            var pom = (tan - 1) / (tan + 1);
            double sin_Beta;
            do
            {
                sin_Beta = pom;
                pom = tan * Math.Exp(0.081696831215303 * Math.Log((1 + 0.081696831215303 * sin_Beta) / (1 - 0.081696831215303 * sin_Beta)));
                pom = (pom - 1) / (pom + 1);
            }
            while (Math.Abs(pom - sin_Beta) > 1e-15);
            var L_JTSK = 2 * Math.Atan(
                (0.420215144586493 * cos_DownVertical - 0.907424504992097 * sin_DownVertical) / (1 + (0.907424504992097 * cos_DownVertical + 0.420215144586493 * sin_DownVertical))
            ) / 1.000597498371542;
            var R_JTSK = Math.Atan(pom / Math.Sqrt(1 - pom * pom));

            // Perpedicular coordinates S-JTSJ
            var e1 = 1 - (1 - 1 / 299.152812853) * (1 - 1 / 299.152812853);
            ro = 6377397.15508 / Math.Sqrt(1 - e1 * Math.Sin(R_JTSK) * Math.Sin(R_JTSK));
            var x1 = (ro + Height) * Math.Cos(R_JTSK) * Math.Cos(L_JTSK);
            var x_w = -4.99821 / 3600 * Math.PI / 180;
            var y1 = (ro + Height) * Math.Cos(R_JTSK) * Math.Sin(L_JTSK);
            var y_w = -1.58676 / 3600 * Math.PI / 180;
            var z1 = ((1 - e1) * ro + Height) * Math.Sin(R_JTSK);
            var z_w = -5.2611 / 3600 * Math.PI / 180;

            // Perpedicular coordinates WGS-84
            var x2 = 570.69 + (1 + 3.543e-6) * (x1 + z_w * y1 - y_w * z1);
            var y2 = 85.69 + (1 + 3.543e-6) * (-z_w * x1 + y1 + x_w * z1);
            var z2 = 462.84 + (1 + 3.543e-6) * (y_w * x1 - x_w * y1 + z1);

            // Geodetic coordinates for WGS84
            var a = 298.257223563 / (298.257223563 - 1);
            var p = Math.Sqrt(x2 * x2 + y2 * y2);
            var e2 = 1 - (1 - 1 / 298.257223563) * (1 - 1 / 298.257223563);
            var theta = Math.Atan(z2 * a / p);
            var st = Math.Sin(theta);
            var ct = Math.Cos(theta);
            tan = (z2 + e2 * a * 6378137.0 * st * st * st) / (p - e2 * 6378137.0 * ct * ct * ct);

            var lat = Math.Atan(tan) / Math.PI * 180;
            var lon = 2 * Math.Atan(y2 / (p + x2)) / Math.PI * 180;
            return new WGS84(lon, lat);
        }
    }
}