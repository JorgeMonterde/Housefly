using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Housefly.Utils
{
    public class ArcSequenceComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ArcSequenceComponent class.
        /// </summary>
        public ArcSequenceComponent()
          : base("ArcSequence", "ArcSequence",
              "Creates a sequence of arcs and optional lines",
              "Housefly", "Utils")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "P", "Base plane to start the sequence", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("Angles", "A", "A list of angles (in degrees) for the arcs of the sequence", GH_ParamAccess.list, new List<double>() { 30, 60, 90 });
            pManager.AddNumberParameter("Radii", "R", "A list of radius for the arcs. Negative values represents the other direction for the arc", GH_ParamAccess.list, new List<double>() { 10, 20, 30 });
            pManager.AddNumberParameter("Lengths", "L", "Optional list of lengths for lines between each arc", GH_ParamAccess.list, new List<double>() { 15, 25, 5 });
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Sequence", "S", "The resulting sequence of arcs and lines as a list", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. Retrieve inputs
            Plane basePlane = Plane.WorldXY;
            List<double> angles = new List<double>();
            List<double> radii = new List<double>();
            List<double> lengths = new List<double>();

            if (!DA.GetData("Plane", ref basePlane)) return;
            if (!DA.GetDataList("Angles", angles)) return;
            if (!DA.GetDataList("Radii", radii)) return;
            if (!DA.GetDataList("Lengths", lengths)) return;

            // 2. Validate inputs
            int turns = angles.Count;

            if (radii.Count != turns)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The number of Radii ({radii.Count}) must match the number of Angles ({turns}).");
                return;
            }

            if (lengths.Count == 0) lengths.Add(0.0);
            while (lengths.Count < turns) lengths.Add(lengths[lengths.Count - 1]);

            // 3. Initialize geometry generation
            List<Curve> sequence = new List<Curve>();
            List<Vector3d> vectors = new List<Vector3d>();
            Point3d currentPt = basePlane.Origin;
            Vector3d xVector = basePlane.XAxis;
            Vector3d yVector = basePlane.YAxis;
            Vector3d zVector = basePlane.ZAxis;

            // 4. Build sequence
            for (int i = 0; i < turns; i++)
            {
                // 4.1. Arc:

                double radius = radii[i];
                if (Math.Abs(radius) < RhinoMath.ZeroTolerance)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A radius cannot be zero.");
                    return;
                }

                double angleInRadians = RhinoMath.ToRadians(angles[i]);
                xVector.Unitize();
                yVector.Unitize();

                // Handle negative radius: flip center side
                double sign = Math.Sign(radius);
                double absRadius = Math.Abs(radius);

                // Build the arc
                Point3d center = currentPt + xVector * (sign * absRadius);
                Plane arcPlane = new Plane(center, xVector * sign, yVector);
                Arc arc = new Arc(arcPlane, absRadius, angleInRadians);

                // Move arc so it starts at currentPt
                Transform moveBack = Transform.Translation(currentPt - arc.StartPoint);
                arc.Transform(moveBack);

                NurbsCurve arcAsNurb = arc.ToNurbsCurve();
                sequence.Add(arcAsNurb);

                // Update the current point, yVector (tangent) and xVector
                currentPt = arc.EndPoint;
                Vector3d tangentVector = arcAsNurb.TangentAtEnd;
                yVector = tangentVector;
                xVector = tangentVector;
                xVector.Rotate(RhinoMath.ToRadians(-90), zVector);

                // 4.2. Line:

                double len = lengths[i];
                Line line = new Line(currentPt, currentPt + yVector * len);
                sequence.Add(line.ToNurbsCurve());

                currentPt = line.To;
                // Direction remains the same for the next segment
            }

            // 5. Output
            DA.SetDataList("Sequence", sequence);
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
            get { return new Guid("7093E4E4-4D32-4D01-94E9-E309324CC208"); }
        }
    }
}