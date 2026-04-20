#pragma warning disable
using System; // Keep for .NET 4.6

namespace BcToolsC.Helpers
{
    // https://www.geospeleos.com/Mapovani/Transformace/WGS_JTSK.pdf
    public static class KrovakHelper
    {
        public readonly struct SJTSK : IEquatable<SJTSK> 
        {
            public double X { get; }
            public string sX { get; }
            public double Y { get; }
            public string sY { get; }
            public double H { get; }
            public double Pos => H;

            public SJTSK(double x, double y, double h)
            {
                X = x; Y = y; H = h;
                sX = string.Format("{0}m", x);
                sY = string.Format("{0}m", y);
            }

            public void Deconstruct(out double x, out double y, out double h)
            {
                x = X; y = Y; h = H;
            }
            public bool Equals(SJTSK other) =>
                X.Equals(other.X) && Y.Equals(other.Y) && H.Equals(other.H);

            public override bool Equals(object obj) =>
                obj is SJTSK other && Equals(other);

            public override int GetHashCode() =>
                X.GetHashCode() ^ Y.GetHashCode() ^ H.GetHashCode();

            public static bool operator ==(SJTSK left, SJTSK right) =>  left.Equals(right);
            public static bool operator !=(SJTSK left, SJTSK right) => !left.Equals(right);

            public override string ToString() => $"X={X}, Y={Y}, H={H}";
        }

        public readonly struct WGS84 : IEquatable<WGS84>
        {
            public double L { get; }
            public double Lon => L;
            public string sL { get; }
            public double B { get; }
            public double Lat => B;
            public string sB { get; }
            public double H { get; }
            public double Pos => H;

            public WGS84(double b, double l, double h)
            {
                B = b; L = l; H = h;
                string vB = "N";
                if (B < 0)
                {
                    B = -B;
                    vB = "S";
                }
                double degB = Math.Floor(B);
                double tmpB = (B - degB) * 60;
                double minB = Math.Floor(tmpB);
                double secB = Math.Round((tmpB - minB) * 60, 3);
                sB = string.Format("{0}°{1}'{2}{3}", degB, minB, secB, vB);
                string vL = "E";
                if (L < 0)
                {
                    L = -L;
                    vL = "W";
                }
                double degL = Math.Floor(L);
                double tmpL = (L - degL) * 60;
                double minL = Math.Floor(tmpL);
                double secL = Math.Round((tmpL - minL) * 60, 3);
                sL = string.Format("{0}°{1}'{2}{3}", degL, minL, secL, vL);
            }

            public void Deconstruct(out double b, out double l, out double h)
            {
                b = B; l = L; h = H;
            }

            public bool Equals(WGS84 other) =>
                B.Equals(other.B) && L.Equals(other.L) && H.Equals(other.H);

            public override bool Equals(object obj) =>
                obj is WGS84 other && Equals(other);

            public override int GetHashCode() =>
                B.GetHashCode() ^ L.GetHashCode() ^ H.GetHashCode();

            public static bool operator ==(WGS84 left, WGS84 right) => left.Equals(right);
            public static bool operator !=(WGS84 left, WGS84 right) => !left.Equals(right);

            public override string ToString() => $"Lat={B}[{sB}], Lon={L}[{sL}], H={H}";
        }

        const double e = 0.081696831215303;
        const double n = 0.97992470462083;
        const double konst_u_ro = 12310230.12797036;
        const double alfa = 1.000597498371542;
        const double sU = 0.863499969506341;
        const double cU = 0.504348889819882;
        const double sV = 0.420215144586493;
        const double cV = 0.907424504992097;
        const double k = 1.003419163966575;
        const double m = 3.543e-6;

        // Výška kvazigeoidu
        const double zeta = 45.0;

        public static
        SJTSK WGS84_SJTSK(WGS84 wG, double H = 245.0) => WGS84_SJTSK(wG.B, wG.L, H); 

        public static 
        SJTSK WGS84_SJTSK(double B, double L, 
            double H = 245.0)
        {
            double ro, t, a, f, e2;
            double db = Math.Abs(B) * Math.PI / 180.0;
            double dl = Math.Abs(L) * Math.PI / 180.0;
            double dh = Math.Abs(H);
            // Pravoúhlé souøadnice S-JTSK
            a = 6378137.0;
            f = 298.257223563;
            e2 = 1 - (1 - 1 / f) * (1 - 1 / f);
            double sinB = Math.Sin(db);
            ro = a / Math.Sqrt(1 - e2 * sinB * sinB);
            // Transformace na WGS-84
            double qx = 570.69, qy = 85.69, qz = 462.84;
            double wx = 4.99821 / 3600.0 * Math.PI / 180.0;
            double wy = 1.58676 / 3600.0 * Math.PI / 180.0;
            double wz = 5.26110 / 3600.0 * Math.PI / 180.0;
            double x = (ro + dh) * Math.Cos(db) * Math.Cos(dl);
            double y = (ro + dh) * Math.Cos(db) * Math.Sin(dl);
            double z = ((1 - e2) * ro + dh) * sinB;
            double dx = x - qx;
            double dy = y - qy;
            double dz = z - qz;
            double scale = 1.0 / (1.0 + m);
            double xn = scale * (dx + wz * dy - wy * dz);
            double yn = scale * (-wz * dx + dy + wx * dz);
            double zn = scale * (wy * dx - wx * dy + dz);
            // Pravoúhlé souøadnice S-JTSK
            a = 6377397.15508;
            f = 299.152812853;
            e2 = 1 - (1 - 1 / f) * (1 - 1 / f);
            double b = f / (f - 1);
            double p = Math.Sqrt(xn * xn + yn * yn);
            double theta = Math.Atan((zn * b) / p);
            double st = Math.Sin(theta);
            double ct = Math.Cos(theta);
            t = (zn + e2 * b * a * st * st * st) /
                (p - e2 * a * ct * ct * ct);
            double Bjtsk = Math.Atan(t);
            double Ljtsk = 2 * Math.Atan(yn / (p + xn));
            double h = Math.Sqrt(1 + t * t) * (p - a / Math.Sqrt(1 + (1 - e2) * t * t));
            sinB = Math.Sin(Bjtsk);
            t = (1 - e * sinB) / (1 + e * sinB);
            t = Math.Pow(1 + sinB, 2) / (1 - sinB * sinB) * Math.Exp(e * Math.Log(t));
            t = 1.00685001861538 * Math.Exp(alfa * Math.Log(t));
            double sinU = (t - 1) / (t + 1);
            double cosU = Math.Sqrt(1 - sinU * sinU);
            double V = alfa * Ljtsk;
            double sinV = Math.Sin(V);
            double cosV = Math.Cos(V);
            double cosDV = cV * cosV + sV * sinV;
            double sinDV = sV * cosV - cV * sinV;
            double sinS = sU * sinU + cU * cosU * cosDV;
            double cosS = Math.Sqrt(1 - sinS * sinS);
            double sinD = sinDV * cosU / cosS;
            double cosD = Math.Sqrt(1 - sinD * sinD);
            double epsilon = n * Math.Atan(sinD / cosD);
            ro = 12310230.12797036 * Math.Exp(-n * Math.Log((1 + sinS) / cosS));
            x = ro * Math.Cos(epsilon);
            y = ro * Math.Sin(epsilon);
            h -= zeta;
            return new SJTSK(x, y, h);
        }

        public static
        WGS84 SJTSK_WGS84(SJTSK sJ, double H = 200) => SJTSK_WGS84(sJ.X, sJ.Y, H);

        public static
        WGS84 SJTSK_WGS84(double X, double Y, 
            double H = 200)
        {
            double ro, t, a, f, e2;
            double dx = Math.Abs(X);
            double dy = Math.Abs(Y);
            double dh = Math.Abs(H) + zeta;
            ro = Math.Sqrt(dx * dx + dy * dy);
            double epislon = 2 * Math.Atan2(dy, ro + dx);
            double D = epislon / n;
            // Sférická šíøka
            double S = 2 * Math.Atan(Math.Exp((1.0 / n) * Math.Log(konst_u_ro / ro))) - Math.PI / 2;
            double sinS = Math.Sin(S);
            double cosS = Math.Cos(S);
            double sinU = sU * sinS - cU * cosS * Math.Cos(D);
            double cosU = Math.Sqrt(1 - sinU * sinU);
            double sinDV = Math.Sin(D) * cosS / cosU;
            double cosDV = Math.Sqrt(1 - sinDV * sinDV);
            double sinV = sV * cosDV - cV * sinDV;
            double cosV = cV * cosDV + sV * sinDV;
            t = Math.Exp((2.0 / alfa) * Math.Log((1 + sinU) / (cosU * k)));
            double pom = (t - 1) / (t + 1);
            double sinB;
            do
            {
                sinB = pom;
                pom = t * Math.Exp(e * Math.Log((1 + e * sinB) / (1 - e * sinB)));
                pom = (pom - 1) / (pom + 1);
            }
            while (Math.Abs(pom - sinB) > 1e-15);
            double _Ljtsk = 2 * Math.Atan2(sinV, 1 + cosV) / alfa;
            double _Bjtsk = Math.Atan2(pom, Math.Sqrt(1 - pom * pom));
            // Pravoúhlé souøadnice S-JTSK
            a = 6377397.15508;
            f = 299.152812853;
            e2 = 1 - (1 - 1 / f) * (1 - 1 / f);
            ro = a / Math.Sqrt(1 - e2 * Math.Sin(_Bjtsk) * Math.Sin(_Bjtsk));
            double x = (ro + dh) * Math.Cos(_Bjtsk) * Math.Cos(_Ljtsk);
            double y = (ro + dh) * Math.Cos(_Bjtsk) * Math.Sin(_Ljtsk);
            double z = ((1 - e2) * ro + dh) * Math.Sin(_Bjtsk);
            // Transformace na WGS-84
            double qx = 570.69, qy = 85.69, qz = 462.84;
            double wx = -4.99821 / 3600 * Math.PI / 180;
            double wy = -1.58676 / 3600 * Math.PI / 180;
            double wz = -5.26110 / 3600 * Math.PI / 180;
            double xn = qx + (1 + m) * (x + wz * y - wy * z);
            double yn = qy + (1 + m) * (-wz * x + y + wx * z);
            double zn = qz + (1 + m) * (wy * x - wx * y + z);
            // Geodetické souøadnice WGS-84
            a = 6378137.0;
            f = 298.257223563;
            e2 = 1 - (1 - 1 / f) * (1 - 1 / f);
            double b = f / (f - 1);
            double p = Math.Sqrt(xn * xn + yn * yn);
            double theta = Math.Atan((zn * b) / p);
            double st = Math.Sin(theta);
            double ct = Math.Cos(theta);
            t = (zn + e2 * b * a * st * st * st) /
                (p - e2 * a * ct * ct * ct);
            double B = Math.Atan(t);
            double L = 2 * Math.Atan2(yn, p + xn);
            var h = Math.Sqrt(1 + t * t) * (p - a / Math.Sqrt(1 + (1 - e2) * t * t));
            var l = L * 180.0 / Math.PI;
            b = B * 180.0 / Math.PI;
            return new WGS84(b, l, h);
        }
    }
}