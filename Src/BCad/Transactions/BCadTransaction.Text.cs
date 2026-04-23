using System; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM 
#if ZWCAD
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;
#else
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
#endif
#endregion

using BcToolsC.Models;

namespace BcToolsC.BCad.Transactions
{
    public partial class BCadTransaction
    {
        public bool TryGetTextStyle(string name, out TextStyleTableRecord textStyle,
            OpenMode mode = OpenMode.ForRead)
        {
            textStyle = null;
            if (!HasRecord(name, Database.TextStyleTableId))
                return false;
            TextStyleTable table = Get<TextStyleTable>(Database.TextStyleTableId);
            return TryGet(table[name], out textStyle, mode);
        }

        public TextStyleTableRecord CreateTextStyle(string name,
            bool bold = false, bool italic = false)
        {
            return GetOrAdd_TextStyle(name, textStyle =>
            {
                if (Annotative)
                    textStyle.Annotative = AnnotativeStates.True;
                textStyle.TextSize = 0.0;
            });
        }

        public MText AddMText(string data, Point2d position,
            double height = 2.5, double width = 0,
            string textStyle = "Standard", double? lineSpacing = null,
            LAYER? layer = null,
            COLOR? color = null,
            ANGLE? angle = null,
            AttachmentPoint vMode = AttachmentPoint.BottomCenter,
            Action<MText> additional = null)
            => AddMText(data, new Point3d(position.X, position.Y, 0),
                height, width, textStyle, lineSpacing,
                layer, color, angle,
                vMode, additional);

        public MText AddMText(string data, Point3d position,
            double height = 2.5, double width = 0,
            string textStyle = "Standard", double? lineSpacing = null,
            LAYER? layer = null,
            COLOR? color = null,
            ANGLE? angle = null,
            // https://help.autodesk.com/view/OARX/2024/ENU/?guid=GUID-FD7EDA56-7FA0-4616-A746-9B97AE0C6456
            AttachmentPoint vMode = AttachmentPoint.BottomCenter,
            Action<MText> additional = null)
        {
            MText entity = new MText
            {
                Contents = data ?? string.Empty,
                Location = position,
                TextHeight = height,
                Rotation = angle ?? 0.0,
                Attachment = vMode,
                FlowDirection = FlowDirection.LeftToRight,
            };
            if (width > 0)
                entity.Width = width;
            if (Annotative)
                entity.Annotative = AnnotativeStates.True;
            MText result = AddToModelSpace(entity); // Musí být v DB dřív
            if (lineSpacing.HasValue)
            {
                result.LineSpacingStyle = LineSpacingStyle.Exactly;
                // Nelze mít menší lineSpacing než 1.0417
                result.LineSpaceDistance = System.Math.Max(1.0417, lineSpacing.Value);
            }
            result.TextStyleId = EnsureTextStyle(textStyle);
            result.LayerId = EnsureLayer(layer);
            result.Color = EnsureColor(color);
            if (additional != null) additional?.Invoke(result);
            return result;
        }

        private ObjectId EnsureTextStyle(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = "Standard";
            if (TryGetTextStyle(name, out TextStyleTableRecord textStyle, OpenMode.ForWrite))
                return textStyle.ObjectId;
            textStyle = CreateTextStyle(name);
            if (textStyle == null || textStyle.ObjectId.IsNull)
            {
                if (TryGetTextStyle("Standard", out textStyle, OpenMode.ForWrite))
                    return textStyle.ObjectId;
                throw new InvalidOperationException($"Failed to create TextStyle '{name}'.");
            }
            return textStyle.ObjectId;
        }
    }
}