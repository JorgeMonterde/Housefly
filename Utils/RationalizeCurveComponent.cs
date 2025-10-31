using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Housefly.Utils
{
    public class RationalizeCurveComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the RationalizeCurveComponent class.
        /// </summary>
        public RationalizeCurveComponent()
          : base("RationalizeCurve", "RationalCurve",
              "Aproximate a curve with another one composed by a sequence of other lines and curves provided",
              "Housefly", "Utils")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("BaseCurve", "BC", "Curve to convert", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Iterations", "I", "Maximum number of iterations (note: avoid '1000' as it may be interpretedaas '1')", GH_ParamAccess.item); // TODO: crashes with "1000" as value
            pManager.AddNumberParameter("Distance", "D", "Acceptable distance for curve aproximation", GH_ParamAccess.item);
            pManager.AddCurveParameter("Curves", "C", "List of curves to be used to aproximate the curve", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("ConvertedCurve", "C", "Converted curve", GH_ParamAccess.item);
            pManager.AddNumberParameter("Distances", "D", "Sum of distances from the end points of all pieces to the base curve", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve baseCurve = null;
            int iterations = 0;
            double maxDistance = 0;
            List<Curve> possibleSegments = new List<Curve>(){};

            if (!DA.GetData(0, ref baseCurve)) return;
            if (!DA.GetData(1, ref iterations)) return;
            if (!DA.GetData(2, ref maxDistance)) return;
            if (!DA.GetDataList(3, possibleSegments)) return;

            this.ValidateInputs(baseCurve, iterations, maxDistance, possibleSegments);


            // auxiliar first segment
            Point3d startPoint = baseCurve.PointAtStart;
            Vector3d tangentAtStart = baseCurve.TangentAtStart;
            tangentAtStart.Reverse();
            Line auxiliarSegment = new Line(startPoint, tangentAtStart, 1); // auxiliar first segment
            auxiliarSegment.Flip();
            List<Curve> curveSegments = new List<Curve>();
            curveSegments.Add(auxiliarSegment.ToNurbsCurve());

            double distancesSum = 0;

            // exit condition
            Point3d endPoint = baseCurve.PointAtEnd;
            bool exitFlag = false;

            // main loop
            for (int i = 0; i < iterations && !exitFlag; i++)
            {
                Curve lastSegment = curveSegments[curveSegments.Count - 1];
                Point3d newStartPoint = lastSegment.PointAtEnd;
                Vector3d newTangent = lastSegment.TangentAtEnd;
                Vector3d newNormal = new Vector3d(newTangent);
                newNormal.Rotate(RhinoMath.ToRadians(90), Vector3d.ZAxis);

                // check exit condition
                exitFlag = this.SetExitFlag(newStartPoint, endPoint, 8, iterations);

                // generate plane for tangent
                Plane planeFromTangent = new Plane(newStartPoint, newTangent, newNormal);

                // direction from segment to base curve
                double t;
                baseCurve.ClosestPoint(newStartPoint, out t);
                Point3d pointOnCurve = baseCurve.PointAt(t);
                distancesSum += newStartPoint.DistanceTo(pointOnCurve);

                // choose best line or arc
                Curve newCurveSegment = this.ChooseSegment(
                  baseCurve,
                  maxDistance,
                  planeFromTangent,
                  possibleSegments
                  );

                curveSegments.Add(newCurveSegment);
            }

            // return outputs
            DA.SetDataList(0, curveSegments);
            DA.SetData(1, distancesSum);
        }

        private void ValidateInputs(Curve baseCurve, int iterations, double maxDistance, List<Curve> possibleSegments)
        {
            if (iterations < 1)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Iterations cannot be smaller than 1");
            }
            if (maxDistance == 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "MaxDistance cannot be equal to 0");
            }
            if (possibleSegments.Count == 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "There must be at least one curve to approximate the other one");
            }
        }


        public class CurveSegment
        {
            public Curve Segment;
            public double Distance;
            public Vector3d BaseCurveTangent;
            public double TangentAngle;

            public CurveSegment(Curve segment, double distance, Vector3d baseCurveTangent)
            {
                this.Segment = segment;
                this.Distance = distance;
                this.BaseCurveTangent = baseCurveTangent;
                this.TangentAngle = Vector3d.VectorAngle(segment.TangentAtEnd, baseCurveTangent);
            }

            // TODO: replace function with sort methods
            public static List<CurveSegment> SortSegmentsByDistance(List<CurveSegment> segments)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (segments[i].Distance < segments[j].Distance)
                        {
                            CurveSegment aux = segments[j];
                            segments[j] = segments[i];
                            segments[i] = aux;
                        }
                    }
                }

                return segments;
            }

            // TODO: replace function with sort methods
            public static List<CurveSegment> SortSegmentsByTangentAngle(List<CurveSegment> segments)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (segments[i].TangentAngle < segments[j].TangentAngle)
                        {
                            CurveSegment aux = segments[j];
                            segments[j] = segments[i];
                            segments[i] = aux;
                        }
                    }
                }

                return segments;
            }
        }

        private Curve ChooseSegment(Curve baseCurve, double maxDistance, Plane originPlane, List<Curve> possibleSegments)
        {
            // 1. mirror and orient all

            List<CurveSegment> segments = new List<CurveSegment>();

            foreach (Curve c in possibleSegments)
            {
                Vector3d perpendicularAtStart = new Vector3d(c.TangentAtStart);
                perpendicularAtStart.Rotate(RhinoMath.ToRadians(90), Vector3d.ZAxis);
                Plane sourcePlane = new Plane(c.PointAtStart, c.TangentAtStart, perpendicularAtStart);

                Plane mirrorPlane = new Plane(c.PointAtStart, c.TangentAtStart, Vector3d.ZAxis);
                Curve mirroredCurve = c.DuplicateCurve();
                mirroredCurve.Transform(Transform.Mirror(mirrorPlane));
                Curve curve = c.DuplicateCurve();

                // orient
                Transform orientTransformation = Transform.PlaneToPlane(sourcePlane, originPlane);
                curve.Transform(orientTransformation);
                mirroredCurve.Transform(orientTransformation);

                // store curve segments
                double t;
                baseCurve.ClosestPoint(curve.PointAtEnd, out t);
                double u;
                baseCurve.ClosestPoint(mirroredCurve.PointAtEnd, out u);

                segments.Add(new CurveSegment(
                  curve,
                  baseCurve.PointAt(t).DistanceTo(curve.PointAtEnd),
                  baseCurve.TangentAt(t)
                  ));

                segments.Add(new CurveSegment(
                  mirroredCurve,
                  baseCurve.PointAt(u).DistanceTo(mirroredCurve.PointAtEnd),
                  baseCurve.TangentAt(u)
                  ));
            }


            // 2. compare

            // 0. none conditions below occure
            // 1. to fit in "maxDistance"
            // 2. if line fits, use line
            // 3. choose arc that is more similar to curve's tangent (angle)

            // filter segments that fit max distance
            List<CurveSegment> segmentsThatFitDistance = segments.FindAll(x => (x as CurveSegment).Distance <= maxDistance);
            if (segmentsThatFitDistance.Count == 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No segment fits max distance");
                List<CurveSegment> sortedSegmentsByDistance = CurveSegment.SortSegmentsByDistance(segments);
                return sortedSegmentsByDistance[0].Segment;
            }

            // sort "segments"
            List<CurveSegment> sortedSegmentsByTangentAngle = CurveSegment.SortSegmentsByTangentAngle(segmentsThatFitDistance);

            return sortedSegmentsByTangentAngle[0].Segment;
        }

        private bool SetExitFlag(Point3d currentPoint, Point3d endPoint, double endThreshold, int iteration)
        {
            // check if it has reached the end
            if (endPoint.DistanceTo(currentPoint) <= endThreshold)
            {
                return true;
            }

            // check if current iteration is admitible
            if (iteration >= 1000)
            {
                return true;
            }

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
            get { return new Guid("A85F9A0B-48F3-4CCB-88CD-491B447785ED"); }
        }
    }
}
