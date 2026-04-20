using System;
using System.Globalization;

using BcToolsC.Helpers;

var culture = (CultureInfo)CultureInfo.GetCultureInfo("cs-CZ").Clone();
culture.NumberFormat.NumberGroupSeparator = "";
culture.NumberFormat.NumberDecimalSeparator = ".";
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

double x = -1166482.5967348684;
double y = -515238.7454294475;
double h = 156.674152016287;
Console.WriteLine(string.Format("{0},{1},{2}", x, y, h));
var jt = KrovakHelper.SJTSK_WGS84(x, y, h);
Console.WriteLine(jt);
// Převod do Google street view
Console.WriteLine(string.Format("https://maps.google.com/maps?q=&layer=c&cbll={0},{1}", jt.B, jt.L));
Console.WriteLine(string.Format("{0},{1},{2}", x, y, h));
double b = jt.B;
double l = jt.L;
h = jt.H;
var wg = KrovakHelper.WGS84_SJTSK(b, l, h);
Console.WriteLine(wg);