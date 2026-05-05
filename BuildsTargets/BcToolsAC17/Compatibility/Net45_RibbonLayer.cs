using Autodesk.Windows;

using RibbonXml;

namespace BcToolsC.Compatibility
{
    public class Net45_RibbonLayerSpecial
        : ControlHandler<RibbonItem>
    {
        public Net45_RibbonLayerSpecial(RibbonItem target, RibbonItemDef _) 
            : base(target, _)
            => target.IsVisible = false;
    }

    public class Net45_RibbonLayerLimited
        : ControlHandler<RibbonItem>
    {
        public Net45_RibbonLayerLimited(RibbonItem target, RibbonItemDef _)
            : base(target, _)
        {
            target.Highlight = Autodesk.Internal.Windows.HighlightMode.Updated;
            target.Description = "Bohužel tato součást je limitována pro:\nAutoCAD v2017";
        }
    }
}