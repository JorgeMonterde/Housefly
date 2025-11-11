using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Housefly.Program
{
    public class PublicRestroomComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PublicRestroomComponent class.
        /// </summary>
        public PublicRestroomComponent()
          : base("PublicRestroomComponent", "PublicRestroom",
              "Creates base plan lines for a public restroom",
              "Housefly", "Program")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Width", "W", "Width of the rectangle", GH_ParamAccess.item);
            pManager.AddNumberParameter("Depth", "D", "Depth of the rectangle", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Entrance", "E", "Entrance line", GH_ParamAccess.item);
            pManager.AddLineParameter("Exterior walls", "EW", "exterior walls", GH_ParamAccess.list);
            pManager.AddLineParameter("Interior partitions", "IP", "interior partitions", GH_ParamAccess.list);
            pManager.AddLineParameter("Cabin doors", "CD", "Door lines for cabin doors", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // IMPROVEMENTS:
            // - Fix toilet door creation for only one cabin
            // - Add door, toilet and sink drawings

            double entranceDepth = 2.0;
            double entranceWidth = 1.5;
            double accessibleCabinDepth = 2.5;
            double accessibleDoorSize = 1.20;
            double minimumCabinWidth = 0.90;
            double minimumCabinDepth = 1.50;

            double width = 0;
            double depth = 0;

            if (!DA.GetData(0, ref width)) return;
            if (!DA.GetData(1, ref depth)) return;

            Point3d A = new Point3d(0, 0, 0);
            Point3d B = new Point3d(width, 0, 0);
            Point3d C = new Point3d(width, depth, 0);
            Point3d D = new Point3d(0, depth, 0);

            Line AB_line = new Line(A, B);
            Line BC_line = new Line(B, C);
            Line CD_line = new Line(C, D);
            Line DA_line = new Line(D, A);

            Vector3d dirBA = A - B;
            dirBA.Unitize();
            Point3d X = B + dirBA * entranceDepth;

            Vector3d perpAB = BC_line.Direction;
            perpAB.Unitize();
            Point3d E = X + perpAB * entranceWidth;

            Line entrance = new Line(E, X);

            // Y = closest point on BC to E
            double tBC = BC_line.ClosestParameter(E);
            Point3d Y = BC_line.PointAt(tBC);

            // main walls
            Line wallAB = AB_line;
            Line wallYC = new Line(Y, C);
            Line wallCD_ = CD_line;
            Line wallDA_ = DA_line;
            Line wallEY = new Line(E, Y);

            // accessible cabin
            Vector3d dirCB = B - C;
            dirCB.Unitize();
            Vector3d accessibleCabinDirection = dirCB * accessibleCabinDepth;
            Point3d Cprime = C + accessibleCabinDirection;
            Point3d Dprime = D + accessibleCabinDirection;

            Vector3d dirDC = C - D;
            dirDC.Unitize();

            Point3d R = Dprime + dirDC * 0.80; // accessible toilet sink side
            Point3d S = R + dirDC * accessibleDoorSize;

            Line DprimeR = new Line(Dprime, R);
            Line RS = new Line(R, S);
            Line SCprime = new Line(S, Cprime);

            // non-accessible cabins

            double available = Y.DistanceTo(Cprime);
            int cabinCount = (int)Math.Floor(available / minimumCabinWidth);

            if (cabinCount < 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not create toilet cabins");
                return;
            }

            double rest = available - cabinCount * minimumCabinWidth;
            double extra = rest / cabinCount;
            double finalWidth = minimumCabinWidth + extra;

            Vector3d dirYC = Cprime - Y;
            dirYC.Unitize();

            Vector3d dirCD = D - C;
            dirCD.Unitize();

            List<Line> cabinWalls = new List<Line>();
            List<Line> cabinDoors = new List<Line>();

            // build cabin partitions
            for (int i = 1; i < cabinCount; i++)
            {
                Point3d Pi = Y + dirYC * (finalWidth * i);
                Line partition = new Line(Pi, Pi + dirCD * minimumCabinDepth);
                cabinWalls.Add(partition);
            }

            // first cabin door
            if (cabinWalls.Count > 0)
            {
                Point3d firstBottom = cabinWalls[0].To;
                Point3d firstProj = wallEY.ClosestPoint(firstBottom, false);
                Line firstDoor = new Line(firstBottom, firstProj);
                cabinDoors.Add(firstDoor);
            }

            // middle cabin doors (between partitions)
            for (int i = 1; i < cabinWalls.Count; i++)
            {
                Point3d prevBottom = cabinWalls[i - 1].To;
                Point3d nextBottom = cabinWalls[i].To;
                cabinDoors.Add(new Line(prevBottom, nextBottom));
            }

            // last cabin door
            if (cabinWalls.Count > 0)
            {
                Point3d lastBottom = cabinWalls[cabinWalls.Count - 1].To;
                Point3d lastProj = SCprime.ClosestPoint(lastBottom, false);
                Line lastDoor = new Line(lastBottom, lastProj);
                cabinDoors.Add(lastDoor);
            }

            cabinWalls.Add(DprimeR);
            cabinWalls.Add(SCprime);
            cabinDoors.Add(RS);

            DA.SetData(0, entrance);
            DA.SetDataList(1, new List<Line>() { wallAB, wallYC, wallCD_, wallDA_, wallEY });
            DA.SetDataList(2, cabinWalls);
            DA.SetDataList(3, cabinDoors);
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
            get { return new Guid("862A9A14-9AFE-457B-9CB7-79835103854C"); }
        }
    }
}