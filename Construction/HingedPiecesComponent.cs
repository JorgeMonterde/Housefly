using Grasshopper.Kernel;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Housefly.Construction
{
    public class HingedPiecesComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the HingedPiecesComponent class.
        /// </summary>
        public HingedPiecesComponent()
          : base("HingedPiecesComponent", "HingedPieces",
              "Creates a polyline as a hinged element with a certain degree of openness",
              "Housefly", "Construction")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("Rail", "L", "The rail that will follow the different pieces.", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Guide plane", "P", "Plane as a guide to generate the hinges.", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddIntegerParameter("Number of pieces", "N", "Number of pieces to divide the opening", GH_ParamAccess.item, 2);
            pManager.AddNumberParameter("Thickness", "T", "The frame thickness", GH_ParamAccess.item, 0.05);
            pManager.AddNumberParameter("Openness", "O", "Degree of openness from 0 to 1", GH_ParamAccess.item, 0.1);
            pManager.AddBooleanParameter("Side", "S", "The side for openness", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Flip rail", "FR", "Wether flip the rail direction or not", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Folded rail", "F", "The resulting folded rail for the folded pieces.", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Piece planes", "P", "List of planes correctly oriented according to frame thickness.", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Line rail = Line.Unset;
            Plane guidePlane = Plane.Unset;
            int numberOfPieces = 0;
            double thickness = 0.0;
            double openness = 0.0;
            bool changeSide = true;
            bool flipRail = false;

            if (!DA.GetData("Rail", ref rail)) return;
            DA.GetData("Guide plane", ref guidePlane);
            DA.GetData("Number of pieces", ref numberOfPieces);
            DA.GetData("Thickness", ref thickness);
            DA.GetData("Openness", ref openness);
            DA.GetData("Side", ref changeSide);
            DA.GetData("Flip rail", ref flipRail);

            if (flipRail) rail.Flip();

            Vector3d railDirection = rail.Direction;
            Vector3d guideVector = railDirection.IsParallelTo(guidePlane.XAxis) == 0 ? guidePlane.XAxis : guidePlane.YAxis;

            // create perpendicular vector to rail that is on the same plane as guideVector
            // perpVector = (A x B) x A
            Vector3d normalForAuxPlane = Vector3d.CrossProduct(railDirection, guideVector);
            Vector3d perpVector = Vector3d.CrossProduct(normalForAuxPlane, railDirection);

            if (!perpVector.IsValid)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Perpendicular vector created is not valid.");
                return;
            }

            if (changeSide) perpVector.Reverse();


            Plane plane = new Plane(rail.From, perpVector, railDirection);
            Plane secondPlane = plane.Clone();


            double pieceLength = rail.Length / numberOfPieces;
            double opennessRotation = 90 * (1 - openness);


            if (!plane.Rotate(RhinoMath.ToRadians(opennessRotation), plane.Normal))
            { 
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not rotate first plane.");
                return;
            }

            if (!secondPlane.Rotate(RhinoMath.ToRadians(- opennessRotation), secondPlane.Normal))
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not rotate second plane.");
                return;
            }

            // create list and add first rectangle
            List<Rectangle3d> rectangles = new List<Rectangle3d>();
            List<Plane> planes = new List<Plane>();

            for (int i = 0; i < numberOfPieces; i++)
            {
                double currentThickness = i % 2 == 0 ? thickness : - thickness;
                Plane currentPlane = i % 2 == 0 ? plane : secondPlane;
                currentPlane.Origin = i == 0 
                    ? plane.Origin : i % 2 == 0 
                    ? rectangles[rectangles.Count - 1].Corner(3) 
                    : rectangles[rectangles.Count - 1].Corner(2);

                Rectangle3d rectangle = new Rectangle3d(currentPlane, currentThickness, pieceLength);
                rectangles.Add(rectangle);
                planes.Add(currentPlane.Clone());
            }

            DA.SetDataList("Folded rail", rectangles);
            DA.SetDataList("Piece planes", new List<Plane>());

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
            get { return new Guid("9CEA58C7-5500-478A-83FD-C73F6A065946"); }
        }
    }
}