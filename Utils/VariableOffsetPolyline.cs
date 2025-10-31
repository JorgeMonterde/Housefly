using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Housefly.Utils
{
    public class VariableOffsetPolyline : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the VariableOffsetPolyline class.
        /// </summary>
        public VariableOffsetPolyline()
          : base("VariableOffsetPolyline", "VariableOffsetPoly",
              "Creates an offset of a polyline with variable distances from the base polyline.",
              "Housefly", "Utils")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Polyline", "P", "Input polyline", GH_ParamAccess.item);
            pManager.AddNumberParameter("Distances", "D", "Offset distances per segment", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tolerance", "T", "Tolerance to be used on line intersections", GH_ParamAccess.item, 0.001);
            pManager.AddBooleanParameter("Both sides", "B", "Wether the offset is done for both sides or not", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Cap", "C", "Wether the offset is returned capped or not", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Offset polylines", "O", "Offset polylines", GH_ParamAccess.list);
            pManager.AddCurveParameter("other", "X", "other", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve auxCurve = null;
            List<double> distances = new List<double>();
            double tol = 0.001;
            bool bothSides = false;
            bool cap = false;
            if (!DA.GetData("Polyline", ref auxCurve)) return;
            if (!DA.GetDataList("Distances", distances)) return;
            if (!DA.GetData("Tolerance", ref tol)) return;
            if (!DA.GetData("Both sides", ref bothSides)) return;
            if (!DA.GetData("Cap", ref cap)) return;

            Polyline pl;
            if (!auxCurve.TryGetPolyline(out pl))
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid curve: Cannot extract a polyline.");
                return;
            }
            if (distances.Count != pl.SegmentCount)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Number of distances must match number of polyline segments.");
                return;
            }

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "FLAG 0");


            List<Line> offsetLinesA = new List<Line>();
            List<Line> offsetLinesB = new List<Line>();

            // TODO: INSIDE THE LOOP THERE IS A BUG THAT IS TRIGGER FOR INDEX OUT OF BOUNDS!
            for (int i = 0; i < pl.SegmentCount; i++)
            {

                // side A

                Point3d a = pl[i];
                Point3d b = pl[i + 1];
                Vector3d dir = b - a;
                dir.Unitize();
                Vector3d perpA = new Vector3d(-dir.Y, dir.X, 0);
                perpA.Unitize();

                double d = distances[i];
                Point3d aOffA = a + perpA * d;
                Point3d bOffA = b + perpA * d;

                offsetLinesA.Add(new Line(aOffA, bOffA));

                if (!bothSides)
                {
                    continue;
                }

                // side B

                Vector3d perpB = new Vector3d(dir.Y, -dir.X, 0);
                perpB.Unitize();

                Point3d aOffB = a + perpB * d;
                Point3d bOffB = b + perpB * d;

                offsetLinesB.Add(new Line(aOffB, bOffB));
            }

            List<Point3d> newPtsA = new List<Point3d>() { offsetLinesA[0].From };
            List<Point3d> newPtsB = new List<Point3d>() { offsetLinesB[0].From };

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "FLAG 1");

            if(offsetLinesA.Count != offsetLinesB.Count) this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "FLAG 2");


            for (int i = 0; i < offsetLinesA.Count; i++)
            {
                Line l1A = offsetLinesA[i];
                Line l2A = offsetLinesA[(i + 1) % offsetLinesA.Count];
                Line l1B = offsetLinesB[i];
                Line l2B = offsetLinesB[(i + 1) % offsetLinesB.Count];
                double t1, t2;
                
                if (i == offsetLinesA.Count - 1 && !pl.IsClosed)
                {
                    newPtsA.Add(l1A.To);
                    if (bothSides) newPtsB.Add(l1B.To);
                    break;
                }

                // side A
                if (!Intersection.LineLine(l1A, l2A, out t1, out t2, tol, false)) // note: tolerance may cause unexpected results depending on use case
                {
                    // could not intersect; return end point of line instead
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Fail intersecting segment of index {i} in side A. End point of segment will be used instead.");
                    newPtsA.Add(l1A.To);
                    continue;
                }

                newPtsA.Add(l1A.PointAt(t1));

                if (!bothSides) continue;

                // side B
                if (!Intersection.LineLine(l1B, l2B, out t1, out t2, tol, false)) // note: tolerance may cause unexpected results depending on use case
                {
                    // could not intersect; return end point of line instead
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Fail intersecting segment of index {i} in side B. End point of segment will be used instead.");
                    newPtsB.Add(l1B.To);
                    continue;
                }

                newPtsB.Add(l1B.PointAt(t1));
            }

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "FLAG 3");


            List<Polyline> result = new List<Polyline>();

            if (pl.IsClosed) // modify first point of polylines if curve is closed
            {
                newPtsA[0] = newPtsA[newPtsA.Count - 1];
                result.Add(new Polyline(newPtsA));
                if (bothSides)
                {
                    newPtsB[0] = newPtsB[newPtsB.Count - 1];
                    result.Add(new Polyline(newPtsB));
                }
            }
            else if (cap) // join polylines if cap
            {
                newPtsB.Reverse();
                newPtsB.Add(newPtsA[0]);
                result.Add(new Polyline(newPtsA.Concat(newPtsB)));
            }
            else // simply return polylines
            {
                result.Add(new Polyline(newPtsA));
                result.Add(new Polyline(newPtsB));
            }

            DA.SetDataList("Offset polylines", result);
            // remove aux
            DA.SetDataList(1, offsetLinesA);
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
            get { return new Guid("EED6B7BA-4788-4FF1-B9FB-D94AAF1B8255"); }
        }
    }
}