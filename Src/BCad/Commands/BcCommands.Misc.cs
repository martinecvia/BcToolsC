using System; // Keep for .NET 4.6

#region O_PROGRAM_DETERMINE_CAD_PLATFORM
#if ZWCAD
using AcApp = ZwSoft.ZwCAD.ApplicationServices;
using AcRun = ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.EditorInput;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices;
using AcRun = Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
#endif
#endregion

using static BcToolsC.BCad.Transactions.BCadTransaction;
using BcToolsC.BCad.Commands.Models;
using BcToolsC.Models;

namespace BcToolsC.BCad.Commands
{
    public partial class BcCommands
    {
        readonly AcRun.RXClass _proxyEntity = AcRun.RXObject.GetClass(typeof(ProxyEntity));
        readonly AcRun.RXClass _proxyObject = AcRun.RXObject.GetClass(typeof(ProxyObject));

        [AcRun.CommandMethod("BCTOOLSC_MC_RP")]
        public void Mc_RemoveProxy()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;

            long n = db.Handseed.Value / 100L;
            if (n == 0) n = 1;
            using (AcRun.ProgressMeter progress = new AcRun.ProgressMeter())
            {
                progress.SetLimit(100);
                progress.Start("Procházím ...");
                // https://forums.autodesk.com/t5/net-forum/proxyobjects-amp-proxyentities-how-to-find-all/td-p/10867012
                var i = 0;
                Call(t =>
                {
                    try
                    {
                        var h = db.BlockTableId.Handle;
                        var l = h.Value;
                        while (true)
                        {
                            long p = l;
                            h = db.Handseed;
                            long c = h.Value;
                            if (p >= c) break;
                            if (l % n == 0L) progress.MeterProgress();
                            if (db.TryGetObjectId(new Handle(l), out ObjectId id) && !id.IsErased)
                            {
                                if (id.ObjectClass.IsDerivedFrom(_proxyEntity))
                                {
                                    if (t.TryGet(id, out ProxyEntity e, OpenMode.ForWrite) && !e.IsErased
                                    // Bit 1 v ProxyFlags určuje, zda je objekt smazatelný
                                    && (e.ProxyFlags & 1) == 1)
                                    {
                                        e.Erase(true);
                                        ++i;
                                    }
                                }
                                else if (id.ObjectClass.IsDerivedFrom(_proxyObject))
                                {
                                    if (t.TryGet(id, out ProxyObject o, OpenMode.ForWrite) && !o.IsErased
                                    // Bit 1 v ProxyFlags určuje, zda je objekt smazatelný
                                    && (o.ProxyFlags & 1) == 1)
                                    {
                                        o.Erase(true);
                                        ++i;
                                    }
                                }
                            }
                            ++l;
                        }
                    }
                    catch (Exception)
                    { }
                });
                progress.Stop();
                editor.Ok("Smazaných proxy objektů: " + i);
            }
        }

        [AcRun.CommandMethod("BCTOOLSC_MC_AC")]
        public void Mc_AlignTextToCurve()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;

            // Výběr textu a křivky
            var __field = GetEntityFromPrompt(editor, "Vyberte text",
            typeof(DBText), typeof(MText));
            if (__field == ObjectId.Null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            var __curve = GetEntityFromPrompt(editor, "Vyberte křivku", 
                typeof(Line), typeof(Polyline), typeof(Polyline2d), typeof(Arc), typeof(Circle), typeof(Spline));
            if (__curve == ObjectId.Null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            using (var t = db.TransactionManager.StartTransaction())
            {
                var curve = t.GetObject(__curve, OpenMode.ForRead) as Curve;
                var field = t.GetObject(__field, OpenMode.ForWrite);
                if (curve == null || field == null)
                {
                    editor.Error("Chyba; Reference neodkazuje na objekt v databázi [E_MEMORY_INVALID].");
                    return;
                }

                // Výchozí parametry
                double o = 1.0;
                double r = 0.0;

                bool running = true;
                while (running)
                {
                    var jig = new Mc_AlignTextToCurve(editor, curve, (Entity)field)
                    {
                        OffsetFactor = o,
                        Rotation = r
                    };
                    PromptResult evResult = editor.Drag(jig);
                    if (evResult.Status == PromptStatus.OK)
                    {
                        t.Commit();
                        running = false;
                    }
                    else if (evResult.Status == PromptStatus.Keyword)
                    {
                        if (evResult.StringResult == "O")
                        {
                            var options = new PromptDoubleOptions("\nZadejte měřítko odsazení: ") { DefaultValue = o };
                            var kwResult = editor.GetDouble(options);
                            if (kwResult.Status == PromptStatus.OK) o = kwResult.Value;
                        }
                        else if (evResult.StringResult == "R")
                        {
                            var options = new PromptAngleOptions("\nZadejte úhel rotace textu: ") { DefaultValue = r };
                            var kwResult = editor.GetAngle(options);
                            if (kwResult.Status == PromptStatus.OK) r = kwResult.Value;
                        }
                    }
                    else
                    {
                        t.Abort();
                        editor.Warn("Výběr byl zrušen uživatelem.");
                        running = false;
                    }
                }
            }
        }

        [AcRun.CommandMethod("BCTOOLSC_MC_SV")]
        public void Mc_SurveyTable()
        {
            if (!BcApp.IsAppProperlyInitialized) return;
            AcApp.Document document = BcApp.Document;
            if (document == null) return;
            Database db = document.Database;
            Editor editor = document.Editor;

            if (!ValidateModelSpace(editor, db)) return;
            var __curve = GetEntityFromPrompt(editor, "Vyberte křivku",
            typeof(Line), typeof(Spline), typeof(Polyline3d), typeof(Polyline2d), typeof(Polyline),
            typeof(Arc), typeof(Circle));
            if (__curve == ObjectId.Null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }

            // Získání vstupu od uživatele
            var __point = GetPointFromPrompt(editor, "Vyberte bod v modelovém prostoru");
            if (__point == null)
            {
                editor.Warn("Výběr byl zrušen uživatelem.");
                return;
            }
            if (!ValidatePointInsideRelief(editor, __point.Value, out Point3d point)) return;
            var scale = GetScaleFromPrompt(editor, "Zadejte měřítko Y", previousScaleY)
                ?? new SCALE(1_000, 1_000);
            previousScaleY = (int)scale.Y;
            Call(t =>
            {
                if (!t.TryGet(__curve, out Curve curve))
                {
                    editor.Warn("Nebyla nalazena žádná data.");
                    return;
                }
                if (curve is Polyline2d) editor.Warn("Křivka je staršího typu Polyline2d; Výsledek nemusí být správný");
                Point3dCollection vertice = GetPolylineVertices(t, curve);
                if (vertice.Count < 2)
                {
                    editor.Warn("Nebyla nalazena žádná data.");
                    return;
                }
                if (curve.Closed) vertice.Add(vertice[0]);

                // Kontrola jestli má smysl vykreslovat část pro výšku Z
                var hasZ = false;
                for (int i = 0; i < vertice.Count; i++)
                {
                    var p = vertice[i];
                    if (Math.Abs(p.Z) > .1)
                    {
                        hasZ = true;
                        break;
                    }
                }

                // Vytvoření tabulky
                Table table = new Table();
                table.SetDatabaseDefaults();
                table.Position = point;

                int cols = hasZ ? 4 : 3;
                int rows = vertice.Count
                // Navíc pro hlavičku
                + 2;
                table.SetSize(rows, cols);
                table.GenerateLayout();
                table.Cells[0, 0].TextString = "Vytyčení";
                table.Cells[1, 0].TextString = "n";
                table.Cells[1, 1].TextString = "Y (m)";
                table.Cells[1, 2].TextString = "X (m)";
                if (hasZ) table.Cells[1, 3].TextString = "Z (m)";
                for (int i = 0; i < vertice.Count; i++)
                {
                    int row = i + 2;
                    Point3d p = vertice[i];
                    table.Cells[row, 0].TextString = (i + 1).ToString(); // N (Index)
                    table.Cells[row, 1].TextString = Math.Abs(p.Y).ToString("F2"); // Y
                    table.Cells[row, 2].TextString = Math.Abs(p.X).ToString("F2"); // X
                    if (hasZ) table.Cells[row, 3].TextString = Math.Abs(p.Z).ToString("F2");
                }
                table.TransformBy(Matrix3d.Scaling(scale.sY, point));
                t.AddToModelSpace(table);
            });
        }
    }
}