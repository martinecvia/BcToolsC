using System; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
#else
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
#endif
#endregion

using BcToolsC.Models;

namespace BcToolsC.BCad.Commands.Models
{
    public sealed class Ge_AlignTextToCurve 
        : EntityJig
    {
        private readonly Editor editor;
        private readonly Curve curve;
        private readonly Entity field;

        private Point3d? __point;
        public double OffsetFactor { get; set; } = 1.0;
        public double Rotation { get; set; } = 0.0;
        private bool Readable { get; set; } = true;

        public Ge_AlignTextToCurve(Editor editor, Curve curve, Entity field) : base(field)
        {
            this.editor = editor;
            this.curve = curve;
            this.field = field;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions options = new JigPromptPointOptions("\nUmístěte text na křivku [Offset(O)/Rotace(R)/Čitelnost(Y)]: ")
            {
                UserInputControls = UserInputControls.Accept3dCoordinates |
                                    UserInputControls.NoNegativeResponseAccepted,
            };
            options.Keywords.Add("O");
            options.Keywords.Add("R");
            options.Keywords.Add("Y");
            PromptPointResult evResult = prompts.AcquirePoint(options);
            if (evResult.Status == PromptStatus.Keyword)
            {
                switch (evResult.StringResult)
                {
                    case "O":
                        return SamplerStatus.OK;
                    case "R":
                        return SamplerStatus.OK;
                    case "Y":
                        Readable = !Readable;
                        return SamplerStatus.OK;
                    default:
                        return SamplerStatus.Cancel;
                }
            } else if (evResult.Status == PromptStatus.OK) {
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
            try
            {
                Point3d closestPoint = curve.GetClosestPointTo(point, false);
                Vector3d t = curve.GetFirstDerivative(closestPoint).GetNormal();
                ANGLE angle = t.AngleOnPlane(new Plane(Point3d.Origin, Vector3d.ZAxis));
                angle += Rotation;
                if (Readable && angle > Math.PI * 0.5 && angle <= Math.PI * 1.5)
                    angle += Math.PI;
                Vector3d n = t.RotateBy(Math.PI / 2, Vector3d.ZAxis);
                TryChangeField(closestPoint, n, angle);
                return true;
            } catch (Exception exception)
            { editor.Error("Chyba; " + exception.Message); }
            return false;
        }

        private void TryChangeField(Point3d point, Vector3d normal, 
            ANGLE a)
        {
            double h;
            Point3d p;
            if (field is DBText dText)
            {
                h = dText.Height;
                p = point + (normal * (OffsetFactor * h));
                dText.Position = p;
                if (dText.Justify != AttachmentPoint.BaseLeft)
                    dText.AlignmentPoint = p;
                dText.Rotation = a;
            } else if (field is MText mText) {
                h = mText.TextHeight;
                p = point + (normal * (OffsetFactor * h));
                mText.Location = p;
                mText.Rotation = a;
            }
        }
    }
}