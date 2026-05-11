namespace BcToolsC.BCad.Commands.Models
{
    public sealed class IntersectedParcel
    {
        public readonly AcDbParcel Parcel;
        public readonly double Area;
        public IntersectedParcel(AcDbParcel parcel, double area)
        {
            Parcel = parcel;
            Area = area;
        }
    }
}