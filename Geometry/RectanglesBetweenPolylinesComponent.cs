using Grasshopper.Kernel;
using Rhino;
using Rhino.Collections;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
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
            // TODO: RETURN LAST POLYGON IF POLYLINES ARE CLOSED 


            Curve curveA = null;
            Curve curveB = null;
            Polyline polyA = null;
            Polyline polyB = null;
            bool flip = false;

            if (!DA.GetData(0, ref curveA)) return;
            if (!DA.GetData(1, ref curveB)) return;
            if (!DA.GetData(2, ref flip)) return;

            if(!curveA.TryGetPolyline(out polyA) || !curveB.TryGetPolyline(out polyB))
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "could not retrieve polylines");
                return;
            };

            if (polyA == null || polyB == null) return;
            if (polyA.Count< 2 || polyB.Count< 2)
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

            Point3d prevP3 = Point3d.Unset; // always on B segment
            Point3d prevP4 = Point3d.Unset; // always on A segment
            Point3d prevOuterIntersectionPt = Point3d.Unset;

            for (int i = 0; i < polyA.SegmentCount; i++)
            {
                Line segA = polyA.SegmentAt(i);
                Line segB = polyB.SegmentAt(i);

                // P1 and P2
                Point3d projA0 = segB.ClosestPoint(segA.From, true); // "true" to ensure the projected point is on the segment
                double tA0 = segB.ClosestParameter(segA.From);
                bool onB_A0 = (tA0 >= 0.0 && tA0 <= 1.0);

                Point3d projB0 = segA.ClosestPoint(segB.From, true); // "true" to ensure the projected point is on the segment
                double tB0 = segA.ClosestParameter(segB.From);
                bool onA_B0 = (tB0 >= 0.0 && tB0 <= 1.0);

                Point3d p1, p2;
                if (onB_A0)
                {
                    // ensure p1 and p2 are always on segments A and B respectivelly
                    p1 = segA.From;
                    p2 = projA0;
                }
                else if (onA_B0)
                {
                    // ensure p1 and p2 are always on segments A and B respectivelly
                    p1 = projB0;
                    p2 = segB.From;
                }
                else
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not get 'p1' and 'p2' on index {i}. Skipping iteration.");
                    continue;
                }

                // P3 and P4
                Point3d projA1 = segB.ClosestPoint(segA.To, true);
                double tA1 = segB.ClosestParameter(segA.To);
                bool onB_A1 = (tA1 >= 0.0 && tA1 <= 1.0);

                Point3d projB1 = segA.ClosestPoint(segB.To, true);
                double tB1 = segA.ClosestParameter(segB.To);
                bool onA_B1 = (tB1 >= 0.0 && tB1 <= 1.0);


                Point3d p3, p4;
                Point3d outerIntersectionPt = Point3d.Unset;
                if (onB_A1)
                {
                    // ensure p3 and p4 are always on segments A and B respectivelly
                    p3 = projA1;
                    p4 = segA.To;
                    outerIntersectionPt = segB.To;
                }
                else if (onA_B1)
                {
                    // ensure p3 and p4 are always on segments A and B respectivelly
                    p3 = segB.To;
                    p4 = projB1;
                    outerIntersectionPt = segA.To;
                }
                else
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not get 'p3' and 'p4' on index {i}. Skipping iteration.");
                    continue;
                }

                // Create closed rectangle polyline
                Polyline rect = new Polyline(new List<Point3d> { p1, p2, p3, p4, p1 });
                if (rect.IsValid && rect.Count == 5) rectangles.Add(rect);

                // Create link polygon
                if (!prevP3.IsValid || !prevP4.IsValid)
                {
                    // iteration 0 does not create polygon as "prevP3" and "prevP4" are not set

                    prevP3 = p3;
                    prevP4 = p4;
                    prevOuterIntersectionPt = outerIntersectionPt;
                    continue;
                }

                // check if new points match previous ones to be aware of the turn between segments
                bool p1Matching = prevP4.DistanceTo(p1) < RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
                bool p2Matching = prevP3.DistanceTo(p2) < RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

                List<Point3d> pointList = null;
                if (p1Matching && p2Matching)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"No link polygon between rectangles on index {i}.");

                    prevP3 = p3;
                    prevP4 = p4;
                    prevOuterIntersectionPt = outerIntersectionPt;
                    continue;
                }
                else if (p1Matching)
                {
                    pointList = new List<Point3d> { prevP4, prevP3, prevOuterIntersectionPt, p2, prevP4 };
                }
                else if (p2Matching)
                {
                    pointList = new List<Point3d> { prevP3, prevP4, prevOuterIntersectionPt, p1, prevP3 };
                }
                else
                {
                    pointList = new List<Point3d> { prevP3, prevP4, p1, p2, prevP3 };
                }

                Polyline polygon = new Polyline(pointList);
                if (polygon.IsValid) polygons.Add(polygon);

                // set new values values for next iteration
                prevP3 = p3;
                prevP4 = p4;
                prevOuterIntersectionPt = outerIntersectionPt;
            }

            if (polyA.IsClosed || polyB.IsClosed)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Currrent implementation does not retrieve last polygon if polylines are closed.");
            }

            DA.SetDataList("Rectangles", rectangles);
            DA.SetDataList("Link polygons", polygons);
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