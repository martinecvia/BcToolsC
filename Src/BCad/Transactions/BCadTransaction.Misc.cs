#region O_PROGRAM_DETERMINE_CAD_PLATFORM 
#if ZWCAD
using AcCol = ZwSoft.ZwCAD.Colors;
#else
using AcCol = Autodesk.AutoCAD.Colors;
#endif
#endregion

namespace BcToolsC.BCad.Transactions
{
    public partial class BCadTransaction
    {
        public AcCol.Color EnsureColor(short? colorIndex)
        {
            if (colorIndex == null || colorIndex > 256 || colorIndex == 256)
                return AcCol.Color.FromColorIndex(AcCol.ColorMethod.ByLayer, 256);
            if (colorIndex <= 0)
                return AcCol.Color.FromColorIndex(AcCol.ColorMethod.ByBlock, 0);
            return AcCol.Color.FromColorIndex(AcCol.ColorMethod.ByAci, colorIndex.Value);
        }
    }
}