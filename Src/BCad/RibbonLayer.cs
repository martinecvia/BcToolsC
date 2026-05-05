using System; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using ZwSoft.Windows;
using ZwSoft.Internal.Windows;
#else
using Autodesk.Windows;
using Autodesk.Internal.Windows;
#endif
#endregion

// RibbonXml
using RibbonXml;

namespace BcToolsC.BCad
{
    public class RibbonLayerHighlight
        : ControlHandler<RibbonItem>
    {
#pragma warning disable CA1416 // Validate platform compatibility
        public RibbonLayerHighlight(RibbonItem target, RibbonItemDef source)
            : base(target, source)
        {
            if (Enum.TryParse(source.Tag, out HighlightMode highlight))
                target.Highlight = highlight;
        }
#pragma warning restore CA1416 // Validate platform compatibility
    }
}