using System; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.GraphicsInterface;
using ZwSoft.ZwCAD.EditorInput;
#else
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.EditorInput;
#endif
#endregion

using BcToolsC.Models;

namespace BcToolsC.BCad.Commands.Models
{
    public sealed class Mc_AlignTextToCurve 
        : EntityJig
    {
        private readonly Editor editor;
        private readonly Curve curve;
        private readonly Entity field;
        private readonly JigPromptPointOptions options;

        private Point3d? __point;

        public double OffsetFactor { get; set; }
        public ANGLE Rotation { get; set; }

        public Mc_AlignTextToCurve(Editor editor, Curve curve, Entity field) : base(field)
        {
            this.editor = editor;
            this.curve = curve;
            this.field = field;
            options = new JigPromptPointOptions("\nUmístěte text na křivku [(O)ffset/(R)otace]: ")
            {
                UserInputControls = UserInputControls.Accept3dCoordinates |
                                    UserInputControls.NoNegativeResponseAccepted,
            };
            options.Keywords.Add("O");
            options.Keywords.Add("R");
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            PromptPointResult evResult = prompts.AcquirePoint(options);
            if (evResult.Status == PromptStatus.Keyword)
                return SamplerStatus.OK;
            else if (evResult.Status == PromptStatus.OK)
            {
                if (__point != null && __point.Value.IsEqualTo(evResult.Value))
                    return SamplerStatus.NoChange;
                __point = evResult.Value;
                return SamplerStatus.OK;
            }
            return SamplerStatus.Cancel;
        }

        protected override bool Update()
        {
            if (__point == null) return false;
            Point3d point = __point.Value;
            point = point.TransformBy(editor.CurrentUserCoordinateSystem);
            try
            {
                var closestPoint = curve.GetClosestPointTo(point, false);
                Vector3d derivative = curve.GetFirstDerivative(closestPoint);
                if (derivative.IsZeroLength()) return false;
                Vector3d t = derivative.GetNormal();
                ANGLE angle = t.AngleOnPlane(new Plane(Point3d.Origin, Vector3d.ZAxis));
                angle += Rotation;
                var flipped = false;
                if (angle > Math.PI * 0.5 && angle <= Math.PI * 1.5)
                {
                    angle += Math.PI;
                    flipped = true;
                }
                Vector3d n = t.RotateBy(Math.PI / 2, Vector3d.ZAxis);
                Vector3d cursorDirection = point - closestPoint;
                int side = cursorDirection.DotProduct(n) >= 0 ? 1 : -1;
                Vector3d offsetDirection = n * side;
                var h = (field is DBText dt) ? dt.Height : ((MText)field).TextHeight;
                var p = closestPoint + (offsetDirection * (OffsetFactor * h));

                // Určení zarovnání (Justification)
                bool shouldUseBottom = side == 1;
                if (flipped) shouldUseBottom = !shouldUseBottom;

                // Zobrazení
                AttachmentPoint pA = shouldUseBottom
                ? AttachmentPoint.BottomCenter
                : AttachmentPoint.TopCenter;
                if (field is DBText dText)
                {
                    dText.Justify = pA;
                    if (pA == AttachmentPoint.BaseLeft)
                        dText.Position = p;
                    else
                        dText.AlignmentPoint = p;
                    dText.Rotation = angle;
                }
                else if (field is MText mText)
                {
                    mText.Location = p;
                    mText.Attachment = pA;
                    mText.Rotation = angle;
                }
                return true;
            } catch { return false; }
        }
    }
}