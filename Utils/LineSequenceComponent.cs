using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Housefly.Utils
{
    public class LineSequenceComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the LineSequenceComponent class.
        /// </summary>
        public LineSequenceComponent()
          : base("LineSequenceComponent", "LineSequence",
              "Creates a polyline from a list of lengths for the segments and angles",
              "Housefly", "Utils")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "P", "Base plane to start the sequence", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("Lengths", "L", "List of lengths for each segment", GH_ParamAccess.list, new List<double>() { 1, 2, 3 });
            pManager.AddNumberParameter("Angles", "A", "A list of angles (in degrees) for each segment turn of the sequence", GH_ParamAccess.list, new List<double>() { 30, 60, 90 });
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Polyline", "P", "The resulting polyline", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Plane basePlane = Plane.WorldXY;
            List<double> lengths = new List<double>();
            List<double> angles = new List<double>();

            if (!DA.GetData("Plane", ref basePlane)) return;
            if (!DA.GetDataList("Lengths", lengths)) return;
            if (!DA.GetDataList("Angles", angles)) return;

            if(lengths.Count != angles.Count)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Lengths and angles count are not equal");
                return;
            }

            Vector3d direction = basePlane.XAxis;
            Point3d startPoint = basePlane.Origin;
            List<Point3d> points = new List<Point3d>();
            points.Add(startPoint);

            for (int i = 0; i < lengths.Count; i++)
            {
                if (lengths[i] == 0)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Lengths cannot be zero");
                    return;
                }

                direction.Unitize();
                direction.Rotate(RhinoMath.ToRadians(angles[i]), basePlane.Normal);
                Vector3d newDirection = direction * lengths[i];
                Point3d newPoint = startPoint + newDirection;
                points.Add(newPoint);

                direction = newDirection;
                startPoint = newPoint;
            }

            DA.SetData("Polyline", new Polyline(points));
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
            get { return new Guid("DE341D87-D464-43E6-BE7F-876BDA435D4C"); }
        }
    }
}