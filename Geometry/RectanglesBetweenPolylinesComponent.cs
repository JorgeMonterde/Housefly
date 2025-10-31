using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Housefly.Geometry
{
    public class RectanglesBetweenPolylinesComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the RectanglesBetweenPolylinesComponent class.
        /// </summary>
        public RectanglesBetweenPolylinesComponent()
          : base("RectanglesBetweenPolylinesComponent", "RectanglesBetweenPolylines",
              "Creates rectangles between two polylines",
              "Housefly", "Geometry")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve A", "A", "First curve (will be converted to a polyline)", GH_ParamAccess.item);
            pManager.AddCurveParameter("Curve B", "B", "Second curve (will be converted to a polyline)", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Flip", "F", "Flip segment pairing direction", GH_ParamAccess.item, false);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Rectangles", "R", "Rectangles formed between corresponding segments", GH_ParamAccess.list);
            pManager.AddCurveParameter("Link polygons", "P", "Polygons between rectangles", GH_ParamAccess.list);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve curveA = null;
            Curve curveB = null;
            Polyline polyA = null;
            Polyline polyB = null;
            bool flip = false;

            if (!DA.GetData(0, ref curveA)) return;
            if (!DA.GetData(1, ref curveB)) return;
            if (!DA.GetData(2, ref flip)) return;

            if (!curveA.TryGetPolyline(out polyA) || !curveA.TryGetPolyline(out polyB))
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not retrieve polylines form curves");
                return;
            }

            if (polyA == null || polyB == null) return;
            if (polyA.Count < 2 || polyB.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Polylines must have at least two points.");
                return;
            }

            if (polyA.SegmentCount != polyB.SegmentCount)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Polylines must have the same number of segments.");
                return;
            }

            // Flip one of the polylines if requested
            if (flip)
                polyB.Reverse();

            List<Polyline> rectangles = new List<Polyline>();
            List<Polyline> polygons = new List<Polyline>();

            Point3d prevP3 = Point3d.Unset;
            Point3d prevP4 = Point3d.Unset;

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "FLAG 0");


            for (int i = 0; i < polyA.SegmentCount; i++)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"FLAG {i}");


                Line segA = polyA.SegmentAt(i);
                Line segB = polyB.SegmentAt(i);

                // --- START PAIR ---
                Point3d projA0 = segB.ClosestPoint(segA.From, true); // "true" to ensure the projected point is on the segment
                double tA0 = segB.ClosestParameter(segA.From);
                bool onB_A0 = (tA0 >= 0.0 && tA0 <= 1.0);

                Point3d projB0 = segA.ClosestPoint(segB.From, true); // "true" to ensure the projected point is on the segment
                double tB0 = segA.ClosestParameter(segB.From);
                bool onA_B0 = (tB0 >= 0.0 && tB0 <= 1.0);

                Point3d p1, p2;
                if (onB_A0)
                {
                    p1 = segA.From;
                    p2 = projA0;
                }
                else if (onA_B0)
                {
                    p1 = segB.From;
                    p2 = projB0;
                }
                else
                {
                    continue;
                }

                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"FLAG {i} - A");


                // --- END PAIR ---
                Point3d projA1 = segB.ClosestPoint(segA.To, true);
                double tA1 = segB.ClosestParameter(segA.To);
                bool onB_A1 = (tA1 >= 0.0 && tA1 <= 1.0);

                Point3d projB1 = segA.ClosestPoint(segB.To, true);
                double tB1 = segA.ClosestParameter(segB.To);
                bool onA_B1 = (tB1 >= 0.0 && tB1 <= 1.0);

                Point3d outerIntersectionPt = Point3d.Unset;

                Point3d p3, p4;
                if (onB_A1)
                {
                    p3 = segA.To;
                    p4 = projA1;
                    outerIntersectionPt = segB.To;
                }
                else if (onA_B1)
                {
                    p3 = segB.To;
                    p4 = projB1;
                    outerIntersectionPt = segA.To;
                }
                else
                {
                    continue;
                }

                // Create closed rectangle polyline
                Polyline rect = new Polyline(new List<Point3d> { p1, p2, p4, p3, p1 });
                if (rect.IsValid && rect.Count == 5) rectangles.Add(rect);



                // Create link polygon
                // check if p1 match with prevP3, as it is always p1 and prevP3 the ones that may coincide
                bool p1Matching = prevP3.Equals(p1);
                // if p2 matches too, polygon will be null
                bool p2Matching = prevP3.Equals(p2);

                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"FLAG {i} - B - {p1Matching.ToString()}, {p2Matching.ToString()}");


                List<Point3d> pointList = null;
                if (p1Matching && p2Matching)
                {
                    // set new p3 and p4 values
                    prevP3 = p3;
                    prevP4 = p4;
                    continue;
                }

                if (p1Matching)
                {
                    pointList = new List<Point3d> { prevP4, prevP3, p2, outerIntersectionPt, prevP4 };
                }
                else
                {
                    pointList = new List<Point3d> { prevP3, prevP4, p2, p1, prevP3 };
                }

                Polyline polygon = new Polyline(pointList);
                if (polygon.IsValid) polygons.Add(polygon);

                // set new p3 and p4 values
                prevP3 = p3;
                prevP4 = p4;

                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"FLAG {i} - C");

            }

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"FLAG END - {rectangles.Count.ToString()}");


            DA.SetDataList(0, rectangles);
        }

        /// <summary>
        /// Robustly convert a Curve to a Polyline:
        /// 1) If already PolylineCurve, extract Polyline.
        /// 2) TryGetPolyline for lines/polylines and some polylike curves.
        /// 3) Fallback: ToPolyline with reasonable tolerances, then extract.
        /// </summary>
        private static bool TryCurveToPolyline(Curve c, double tol, out Polyline pl)
        {
            pl = new Polyline();

            if (c == null) return false;

            // PolylineCurve direct path
            if (c is PolylineCurve plc)
            {
                if (plc.TryGetPolyline(out pl)) return true;
            }

            // Many curves (including polylines and lines) succeed here
            if (c.TryGetPolyline(out pl)) return true;

            // Fallback: tessellate to a polyline, then extract
            // angleToleranceRadians, chordTolerance, minEdgeLength, maxEdgeLength
            double angleTolRad = RhinoMath.ToRadians(3.0); // fairly tight
            double chordTol = tol * 0.5;                   // tighter than doc tol
            double minLen = 0.0;
            double maxLen = double.MaxValue;

            PolylineCurve approx = c.ToPolyline(angleTolRad, chordTol, minLen, maxLen);
            if (approx != null && approx.TryGetPolyline(out pl))
                return true;

            return false;
        }
    

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("C84972B4-0AB0-434E-8602-8DD8EAFA2BDC"); }
        }
    }
}